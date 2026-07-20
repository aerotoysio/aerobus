using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IConnectionRules
    {
        Task<ConnectionRule?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default);

        Task<IReadOnlyList<ConnectionRule>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default);

        // Main list operation (paged + search)
        Task<IReadOnlyList<ConnectionRule>> ListByCompanyAsync(
            Guid companyId,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default);

        Task<ConnectionRule?> SaveAsync(
            ConnectionRule model,
            CancellationToken ct = default);

        Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default);
    }

    public sealed class ConnectionRules(IDocumentStore store) : DocumentRepository<ConnectionRule>(store), IConnectionRules
    {
        protected override string Collection => DfCollections.Catalogue.ConnectionRules;

        public Task<IReadOnlyList<ConnectionRule>> ListByCompanyAsync(
            Guid companyId,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            QueryAsync(Eq(Df.Field(nameof(ConnectionRule.CompanyId)), companyId), pageNumber, pageSize, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
