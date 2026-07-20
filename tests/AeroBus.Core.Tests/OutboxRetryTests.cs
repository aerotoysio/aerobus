using AeroBus.Core.Data;
using AeroBus.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Retry/backoff/dead-letter behaviour of the dispatcher. A webhook that always
/// fails (an unroutable URL) must: advance Attempts and push NextAttemptAt out by
/// an exponential backoff on each attempt, and — after MaxAttempts — park the row
/// as Dead so it stops being retried. Driven deterministically via
/// <see cref="OutboxDispatcher.PumpOnceAsync"/> (no timer).
/// </summary>
[Collection("documentforge")]
public class OutboxRetryTests(DocumentForgeFixture fx)
{
    // A tiny DI graph so the dispatcher can resolve its scoped repos + delivery.
    private ServiceProvider BuildProvider(EventsOptions opts)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fx.Client);
        services.AddSingleton(fx.Store);
        services.AddSingleton<IOptions<EventsOptions>>(new OptionsWrapper<EventsOptions>(opts));
        services.AddScoped<IOutbox>(_ => new Outbox(fx.Store, fx.Client));
        services.AddScoped<IWebhookSubscriptions>(_ => new WebhookSubscriptions(fx.Store));
        // The dispatcher pumps whatever databases the target source lists; here,
        // just the fixture's single database.
        services.AddScoped<IEventPumpTargets>(_ => new StaticEventPumpTargets(
            new EventPumpTarget("test", new Outbox(fx.Store, fx.Client), new WebhookSubscriptions(fx.Store))));
        // Delivery over a short-timeout HttpClient; the target URL is unroutable so
        // every POST fails fast.
        services.AddScoped<IWebhookDelivery>(_ =>
            new WebhookDelivery(new HttpClient { Timeout = TimeSpan.FromSeconds(2) }, NullLogger<WebhookDelivery>.Instance));
        services.AddSingleton<OutboxDispatcher>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Failing_delivery_advances_attempts_backoff_then_dead()
    {
        var company = DocumentForgeFixture.NewCompany();
        var opts = new EventsOptions
        {
            MaxAttempts = 3,
            BackoffBaseSeconds = 2,
            BackoffCapSeconds = 300,
            BatchSize = 10,
        };
        var provider = BuildProvider(opts);
        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        var publisher = EventsTestHelpers.Publisher(fx);
        var subs = new WebhookSubscriptions(fx.Store);
        var outbox = EventsTestHelpers.Outbox(fx);

        // A subscription whose URL cannot be reached → delivery always fails.
        var sub = await subs.SaveAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Url = "http://127.0.0.1:9/hook", // discard port, connection refused
            Types = new[] { "retry.*" },
            Secret = "s3cr3t",
            Active = true,
        });

        var evt = await publisher.PublishAsync(
            "retry.test", new EventSubject("tests", Guid.NewGuid().ToString()), new { }, company);
        Assert.NotNull(evt);

        // Attempt 1: claim → deliver (fails) → Failed, Attempts=1, NextAttemptAt set.
        await dispatcher.PumpOnceAsync();
        var a1 = await outbox.GetByIdAsync(evt!.Id);
        Assert.NotNull(a1);
        Assert.Equal(OutboxStatus.Failed, a1!.Status);
        Assert.Equal(1, a1.Attempts);
        Assert.NotNull(a1.NextAttemptAt);
        var next1 = a1.NextAttemptAt!.Value;
        Assert.True(next1 > DateTime.UtcNow.AddSeconds(-1), "NextAttemptAt should be in the (near) future.");

        // The row is now Failed with NextAttemptAt in the future, so a pump BEFORE it
        // is due must NOT pick it up (Attempts stays 1). This proves the backoff gate.
        await dispatcher.PumpOnceAsync();
        var stillWaiting = await outbox.GetByIdAsync(evt.Id);
        Assert.Equal(1, stillWaiting!.Attempts);

        // Force the row due by rewinding NextAttemptAt, then pump: Attempts→2,
        // and the backoff for attempt 2 is strictly larger than for attempt 1.
        await outbox.SaveAsync(stillWaiting with { NextAttemptAt = DateTime.UtcNow.AddSeconds(-1) });
        await dispatcher.PumpOnceAsync();
        var a2 = await outbox.GetByIdAsync(evt.Id);
        Assert.Equal(OutboxStatus.Failed, a2!.Status);
        Assert.Equal(2, a2.Attempts);
        var backoff1 = next1 - a1.Updated!.Value;          // ~2s window
        var backoff2 = a2.NextAttemptAt!.Value - a2.Updated!.Value; // ~4s window
        Assert.True(backoff2 > backoff1, $"backoff should grow: {backoff2} !> {backoff1}");

        // Force due again and pump: this is attempt 3 == MaxAttempts → Dead.
        await outbox.SaveAsync(a2 with { NextAttemptAt = DateTime.UtcNow.AddSeconds(-1) });
        await dispatcher.PumpOnceAsync();
        var dead = await outbox.GetByIdAsync(evt.Id);
        Assert.Equal(OutboxStatus.Dead, dead!.Status);
        Assert.Equal(3, dead.Attempts);
        Assert.Null(dead.NextAttemptAt);

        // A Dead row is no longer dispatchable — a further pump leaves it alone.
        await dispatcher.PumpOnceAsync();
        var stillDead = await outbox.GetByIdAsync(evt.Id);
        Assert.Equal(OutboxStatus.Dead, stillDead!.Status);
        Assert.Equal(3, stillDead.Attempts);

        // Cleanup.
        await outbox.DeleteAsync(evt.Id);
        await subs.DeleteAsync(sub!.Id);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Event_with_no_matching_subscription_dispatches_immediately()
    {
        var company = DocumentForgeFixture.NewCompany();
        var provider = BuildProvider(new EventsOptions { MaxAttempts = 3 });
        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        var publisher = EventsTestHelpers.Publisher(fx);
        var outbox = EventsTestHelpers.Outbox(fx);

        // No subscriptions for this company → nothing to deliver to → Dispatched.
        var evt = await publisher.PublishAsync(
            "nomatch.test", new EventSubject("tests", Guid.NewGuid().ToString()), new { }, company);
        await dispatcher.PumpOnceAsync();

        var after = await outbox.GetByIdAsync(evt!.Id);
        Assert.Equal(OutboxStatus.Dispatched, after!.Status);
        Assert.NotNull(after.DispatchedAt);

        await outbox.DeleteAsync(evt.Id);
        await provider.DisposeAsync();
    }
}
