using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface ICountries
    {
        Task<Country?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Country>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Country>> GetByContinentAsync(Guid continentId, CancellationToken ct = default);
        Task<IReadOnlyList<Country>> ListByCompanyAsync(
            Guid companyId, Guid? continentId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Country?> SaveAsync(Country model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Countries(IDocumentStore store) : DocumentRepository<Country>(store), ICountries
    {
        protected override string Collection => DfCollections.Catalogue.Countries;

        public Task<IReadOnlyList<Country>> GetByContinentAsync(Guid continentId, CancellationToken ct = default) =>
            QueryAsync(Eq("continentId", continentId), ct: ct);

        public Task<IReadOnlyList<Country>> ListByCompanyAsync(
            Guid companyId, Guid? continentId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["companyId"] = companyId };
            if (continentId is { } cid) f["continentId"] = cid;
            if (!string.IsNullOrWhiteSpace(status)) f["status"] = status;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
