using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IMarketZones
    {
        Task<MarketZone?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<MarketZone>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<MarketZone>> SearchAsync(
            Guid? companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<MarketZone?> SaveAsync(MarketZone model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class MarketZones(IDocumentStore store) : DocumentRepository<MarketZone>(store), IMarketZones
    {
        protected override string Collection => "marketzones";

        // Selectors are embedded in the zone, so save/load carry them in one round trip.
        // Compiling selectors -> IncludedAirports lives in MarketZoneService.Build
        // (it needs the airport/country geo data).

        public Task<IReadOnlyList<MarketZone>> SearchAsync(
            Guid? companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?>();
            if (companyId is { } cid) f["CompanyId"] = cid;
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
