using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IRegions
    {
        Task<Region?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Region>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Region>> GetByCountryAsync(Guid countryId, CancellationToken ct = default);
        Task<IReadOnlyList<Region>> ListByCompanyAsync(
            Guid companyId, Guid? countryId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Region?> SaveAsync(Region model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Regions(IDocumentStore store) : DocumentRepository<Region>(store), IRegions
    {
        protected override string Collection => "regions";

        public Task<IReadOnlyList<Region>> GetByCountryAsync(Guid countryId, CancellationToken ct = default) =>
            QueryAsync(Eq("CountryId", countryId), ct: ct);

        public Task<IReadOnlyList<Region>> ListByCompanyAsync(
            Guid companyId, Guid? countryId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["CompanyId"] = companyId };
            if (countryId is { } cid) f["CountryId"] = cid;
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
