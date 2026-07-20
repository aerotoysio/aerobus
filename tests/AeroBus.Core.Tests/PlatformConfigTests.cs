using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Platform settings live in the database: round-trip a plain and a secret key
/// through <see cref="PlatformConfigService"/> against real DocumentForge and
/// assert (a) secrets are encrypted at rest and masked on list, (b) reads
/// decrypt, (c) the RuleForge settings provider prefers database values and
/// falls back to the appsettings bootstrap.
/// </summary>
[Collection("documentforge")]
public class PlatformConfigTests(DocumentForgeFixture fx)
{
    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private PlatformConfigService BuildService() =>
        new(new TestPlatformConfigs(fx),
            new EphemeralDataProtectionProvider(),
            new NullEventPublisher(),
            NullLogger<PlatformConfigService>.Instance);

    // The repo hard-wires the keyed control store in production; tests run it
    // against the fixture's single database instead.
    private sealed class TestPlatformConfigs(DocumentForgeFixture fx) : IPlatformConfigs
    {
        private readonly PlatformConfigs _inner = new(fx.Store);
        public Task<Core.Model.Admin.PlatformConfig?> GetByKeyAsync(string key, CancellationToken ct = default) => _inner.GetByKeyAsync(key, ct);
        public Task<IReadOnlyList<Core.Model.Admin.PlatformConfig>> GetAllAsync(CancellationToken ct = default) => _inner.GetAllAsync(ct);
        public Task<Core.Model.Admin.PlatformConfig?> SaveAsync(Core.Model.Admin.PlatformConfig model, CancellationToken ct = default) => _inner.SaveAsync(model, ct);
        public Task<bool> DeleteByKeyAsync(string key, CancellationToken ct = default) => _inner.DeleteByKeyAsync(key, ct);
    }

    private sealed class NullEventPublisher : Core.Events.IEventPublisher
    {
        public Task<Core.Events.OutboxEvent?> PublishAsync(
            string type, Core.Events.EventSubject subject, object data, Guid? companyId, string? actor = null, CancellationToken ct = default) =>
            Task.FromResult<Core.Events.OutboxEvent?>(null);
    }

    [Fact]
    public async Task Plain_value_round_trips_and_lists_unmasked()
    {
        var svc = BuildService();
        var key = $"test.plain.{Guid.NewGuid():N}";

        await svc.SetAsync(key, "hello-settings", isSecret: false, "a plain test key", actor: "tests");
        Assert.Equal("hello-settings", await svc.GetAsync(key));

        var listed = (await svc.ListAsync()).Single(v => v.Key == key);
        Assert.Equal("hello-settings", listed.Value);
        Assert.False(listed.IsSecret);

        Assert.True(await svc.DeleteAsync(key, actor: "tests"));
        Assert.Null(await svc.GetAsync(key));
    }

    [Fact]
    public async Task Secret_is_encrypted_at_rest_and_masked_on_list()
    {
        var svc = BuildService();
        var repo = new TestPlatformConfigs(fx);
        var key = $"test.secret.{Guid.NewGuid():N}";
        const string secret = "s3cr3t-value";

        await svc.SetAsync(key, secret, isSecret: true, "a secret test key", actor: "tests");

        // Decrypted through the service…
        Assert.Equal(secret, await svc.GetAsync(key));

        // …but never stored or listed in plaintext.
        var raw = await repo.GetByKeyAsync(key);
        Assert.NotNull(raw);
        Assert.NotEqual(secret, raw!.Value);
        Assert.True(raw.IsSecret);

        var listed = (await svc.ListAsync()).Single(v => v.Key == key);
        Assert.Equal(PlatformConfigService.SecretMask, listed.Value);
        Assert.True(listed.IsSecret);

        await svc.DeleteAsync(key, actor: "tests");
    }

    [Fact]
    public async Task RuleForge_settings_prefer_database_and_fall_back_to_bootstrap()
    {
        var svc = BuildService();
        var bootstrap = new Opt<RuleForgeOptions>(new RuleForgeOptions
        {
            BaseUrl = "http://bootstrap:5050",
            ApiKey = "bootstrap-key",
            TimeoutMs = 1234,
        });
        var provider = new PlatformRuleForgeSettingsProvider(svc, bootstrap);

        // Nothing in the database → bootstrap wins.
        var s1 = await provider.GetAsync();
        Assert.Equal("http://bootstrap:5050", s1.BaseUrl);
        Assert.Equal("bootstrap-key", s1.ApiKey);
        Assert.Equal(1234, s1.TimeoutMs);

        // Database values (one plain, one secret) take precedence.
        await svc.SetAsync(PlatformRuleForgeSettingsProvider.BaseUrlKey, "http://db:6060", false, null, "tests");
        await svc.SetAsync(PlatformRuleForgeSettingsProvider.ApiKeyKey, "db-key", true, null, "tests");
        var s2 = await provider.GetAsync();
        Assert.Equal("http://db:6060", s2.BaseUrl);
        Assert.Equal("db-key", s2.ApiKey);
        Assert.Equal(1234, s2.TimeoutMs); // still bootstrap — unset in db

        await svc.DeleteAsync(PlatformRuleForgeSettingsProvider.BaseUrlKey, "tests");
        await svc.DeleteAsync(PlatformRuleForgeSettingsProvider.ApiKeyKey, "tests");
    }
}
