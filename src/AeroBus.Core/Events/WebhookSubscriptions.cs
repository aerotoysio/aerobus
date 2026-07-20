using AeroBus.Core.Data;

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
    // Subscriptions are airline-owned: they live in the org's own database next
    // to its outbox (EventStores picks the database per request; the dispatcher
    // constructs one per database it pumps).
    public sealed class WebhookSubscriptions(IDocumentStore store)
        : DocumentRepository<WebhookSubscription>(store), IWebhookSubscriptions
    {
        protected override string Collection => DfCollections.Events.WebhookSubscriptions;

        public Task<IReadOnlyList<WebhookSubscription>> GetActiveAsync(CancellationToken ct = default) =>
            QueryAsync(Eq(Df.Field(nameof(WebhookSubscription.Active)), true), ct: ct);
    }
}
