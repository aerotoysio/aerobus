using AeroBus.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Events
{
    public interface IWebhookSubscriptions
    {
        Task<WebhookSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<WebhookSubscription>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<WebhookSubscription>> GetActiveAsync(CancellationToken ct = default);
        Task<WebhookSubscription?> SaveAsync(WebhookSubscription model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    /// <summary>
    /// Webhook subscription documents (<c>webhooksubscriptions</c> collection).
    /// The dispatcher reads the active set on each delivery to fan an event out to
    /// every matching URL; the <c>/events/subscriptions</c> endpoints manage them
    /// per company.
    /// </summary>
    public sealed class WebhookSubscriptions(
        [FromKeyedServices(AeroBus.Core.Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentStore store)
        : DocumentRepository<WebhookSubscription>(store), IWebhookSubscriptions
    {
        protected override string Collection => DfCollections.Events.WebhookSubscriptions;

        public Task<IReadOnlyList<WebhookSubscription>> GetActiveAsync(CancellationToken ct = default) =>
            QueryAsync(Eq("Active", true), ct: ct);
    }
}
