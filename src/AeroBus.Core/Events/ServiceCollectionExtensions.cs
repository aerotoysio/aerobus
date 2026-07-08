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

            // Publisher + repos are scoped like the other domain services; their
            // internal cursor-id cache is process-wide (static), so scope is fine.
            services.AddScoped<IEventPublisher, EventPublisher>();
            services.AddScoped<IOutbox, Outbox>();
            services.AddScoped<IWebhookSubscriptions, WebhookSubscriptions>();

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
