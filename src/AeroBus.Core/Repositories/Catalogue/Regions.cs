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
        protected override string Collection => DfCollections.Catalogue.Regions;

        public Task<IReadOnlyList<Region>> GetByCountryAsync(Guid countryId, CancellationToken ct = default) =>
            QueryAsync(Eq(Df.Field(nameof(Region.CountryId)), countryId), ct: ct);

        public Task<IReadOnlyList<Region>> ListByCompanyAsync(
            Guid companyId, Guid? countryId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { [Df.Field(nameof(Region.CompanyId))] = companyId };
            if (countryId is { } cid) f[Df.Field(nameof(Region.CountryId))] = cid;
            if (!string.IsNullOrWhiteSpace(status)) f[Df.Field(nameof(Region.Status))] = status;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
