using AeroBus.Core.Data;
using AeroBus.Core.Model.Distribution;

namespace AeroBus.Core.Repositories.Distribution
{
    public interface IOffers
    {
        Task<Offer?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Offer>> GetBySearchAsync(Guid searchId, CancellationToken ct = default);
        Task<Offer?> SaveAsync(Offer model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public sealed class Offers(IDocumentStore store) : DocumentRepository<Offer>(store), IOffers
    {
        protected override string Collection => "offers";

        public Task<IReadOnlyList<Offer>> GetBySearchAsync(Guid searchId, CancellationToken ct = default) =>
            QueryAsync(Eq("SearchId", searchId), ct: ct);

        // events: offer.created via outbox in Phase 6
    }
}
