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
        protected override string Collection => DfCollections.Catalogue.MarketZones;

        // Selectors are embedded in the zone, so save/load carry them in one round trip.
        // Compiling selectors -> IncludedAirports lives in MarketZoneService.Build
        // (it needs the airport/country geo data).

        public Task<IReadOnlyList<MarketZone>> SearchAsync(
            Guid? companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var clauses = new List<string>();
            if (companyId is { } cid) clauses.Add($"{Df.Field(nameof(MarketZone.CompanyId))} = '{cid}'");
            if (!string.IsNullOrWhiteSpace(status))
                clauses.Add($"{Df.Field(nameof(MarketZone.Status))} = '{status.Replace("'", "''")}'");
            if (!string.IsNullOrWhiteSpace(search))
                clauses.Add(Df.Match(search, Df.Field(nameof(MarketZone.Name)), Df.Field(nameof(MarketZone.Description))));
            return QueryWhereAsync(string.Join(" AND ", clauses), pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
