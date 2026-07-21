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
            var where = $"{Df.Field(nameof(StockKeeper.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(status))
                where += $" AND {Df.Field(nameof(StockKeeper.Status))} = '{status.Replace("'", "''")}'";
            if (!string.IsNullOrWhiteSpace(category))
                where += $" AND {Df.Field(nameof(StockKeeper.Category))} = '{category.Replace("'", "''")}'";
            if (!string.IsNullOrWhiteSpace(type))
                where += $" AND {Df.Field(nameof(StockKeeper.Type))} = '{type.Replace("'", "''")}'";
            if (!string.IsNullOrWhiteSpace(scope))
                where += $" AND {Df.Field(nameof(StockKeeper.Scope))} = '{scope.Replace("'", "''")}'";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND " + Df.Match(search, Df.Field(nameof(StockKeeper.Name)), Df.Field(nameof(StockKeeper.Category)));
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
