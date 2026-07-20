using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IAirports
    {
        Task<Airport?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Airport>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Airport>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Airport?> SaveAsync(Airport model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Airports(IDocumentStore store) : DocumentRepository<Airport>(store), IAirports
    {
        protected override string Collection => DfCollections.Catalogue.Airports;

        // GetById / GetByCompany / Save come from DocumentRepository<Airport>.

        public Task<IReadOnlyList<Airport>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var where = $"{Df.Field(nameof(Airport.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(search))
            {
                // DF LIKE is case-insensitive; match code, name or city.
                var q = Df.Contains(search);
                where += $" AND ({Df.Field(nameof(Airport.Code))} LIKE '{q}'" +
                         $" OR {Df.Field(nameof(Airport.Name))} LIKE '{q}'" +
                         $" OR {Df.Field(nameof(Airport.City))} LIKE '{q}')";
            }
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
