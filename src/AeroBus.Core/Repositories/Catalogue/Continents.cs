using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IContinents
    {
        Task<Continent?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Continent>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Continent>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Continent?> SaveAsync(Continent model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Continents(IDocumentStore store) : DocumentRepository<Continent>(store), IContinents
    {
        protected override string Collection => DfCollections.Catalogue.Continents;

        public Task<IReadOnlyList<Continent>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var where = $"{Df.Field(nameof(Continent.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND " + Df.Match(search, Df.Field(nameof(Continent.Code)), Df.Field(nameof(Continent.Name)));
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
