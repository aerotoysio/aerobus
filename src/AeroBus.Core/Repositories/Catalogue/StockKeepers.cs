using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IStockKeepers
    {
        Task<StockKeeper?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default);

        Task<IReadOnlyList<StockKeeper>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default);

        // Main list – paged + filters + search
        Task<IReadOnlyList<StockKeeper>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? category,
            string? type,
            string? scope,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default);

        Task<StockKeeper?> SaveAsync(
            StockKeeper model,
            CancellationToken ct = default);

        Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default);
    }

    public sealed class StockKeepers(IDocumentStore store) : DocumentRepository<StockKeeper>(store), IStockKeepers
    {
        protected override string Collection => DfCollections.Catalogue.StockKeepers;

        public Task<IReadOnlyList<StockKeeper>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? category,
            string? type,
            string? scope,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["companyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(status)) f["status"] = status;
            if (!string.IsNullOrWhiteSpace(category)) f["category"] = category;
            if (!string.IsNullOrWhiteSpace(type)) f["type"] = type;
            if (!string.IsNullOrWhiteSpace(scope)) f["scope"] = scope;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
