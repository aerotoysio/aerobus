using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class StockKeeperService(IStockKeepers repo)
    {
        private readonly IStockKeepers _repo = repo;

        public Task<StockKeeper?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<StockKeeper>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<StockKeeper>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? category,
            string? type,
            string? scope,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(
                companyId,
                status,
                category,
                type,
                scope,
                search,
                pageNumber,
                pageSize,
                ct);

        public Task<StockKeeper?> SaveAsync(
            StockKeeper model,
            CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
