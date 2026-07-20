using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Events
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Wires the event backbone: the outbox publisher (domain services depend on
        /// <see cref="IEventPublisher"/>), the outbox + webhook-subscription repos,
        /// the signed-webhook delivery client, and the background
        /// <see cref="OutboxDispatcher"/>. Options bind from the <c>Events</c>
        /// config section (all defaulted).
        /// </summary>
        public static IServiceCollection AddEvents(this IServiceCollection services, IConfiguration config)
        {
            services.Configure<EventsOptions>(config.GetSection("Events"));

            // Events are airline-specific: EventStores picks the request's event
            // database (the org's own db when the tenant middleware resolved one,
            // control for platform paths), and publisher + repos are built over
            // that pick. Scoped like the other domain services; the publisher's
            // cursor-id cache is process-wide (static, partitioned per database).
            services.AddScoped<IEventStores, EventStores>();
            services.AddScoped<IEventPublisher>(sp => new EventPublisher(
                sp.GetRequiredService<IEventStores>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EventPublisher>>()));
            services.AddScoped<IOutbox>(sp =>
            {
                var stores = sp.GetRequiredService<IEventStores>();
                return new Outbox(stores.Store, stores.Client);
            });
            services.AddScoped<IWebhookSubscriptions>(sp =>
                new WebhookSubscriptions(sp.GetRequiredService<IEventStores>().Store));

            // What the dispatcher pumps: control + every Active registered org.
            services.AddScoped<IEventPumpTargets, RegistryEventPumpTargets>();

            // Typed HttpClient for webhook delivery, with the configured timeout.
            services.AddHttpClient<IWebhookDelivery, WebhookDelivery>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<EventsOptions>>().Value;
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.WebhookTimeoutSeconds));
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AeroBus-Webhooks", "1.0"));
            });

            services.AddHostedService<OutboxDispatcher>();

            return services;
        }
    }
}
