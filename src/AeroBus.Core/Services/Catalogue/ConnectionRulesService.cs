using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class ConnectionRulesService(IConnectionRules repo)
    {
        private readonly IConnectionRules _repo = repo;

        public Task<ConnectionRule?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<ConnectionRule>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<ConnectionRule>> ListByCompanyAsync(
            Guid companyId,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, search, pageNumber, pageSize, ct);

        public Task<ConnectionRule?> SaveAsync(
            ConnectionRule model,
            CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
