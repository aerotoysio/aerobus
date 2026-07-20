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
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default) =>
            // NOTE: free-text `search` is not yet applied (DocumentForge equality
            // can't do LIKE); the relational path ignored it too. Revisit if the
            // admin needs server-side airport search across the ~9k rows.
            QueryAsync(Eq(Df.Field(nameof(Airport.CompanyId)), companyId), pageNumber, pageSize, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
