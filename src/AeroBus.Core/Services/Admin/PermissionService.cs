using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    public sealed class PermissionService(IPermissions repo)
    {
        private readonly IPermissions _repo = repo;

        public Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct = default) => _repo.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<Permission>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
            => _repo.GetByCompanyAsync(companyId, ct);
        public Task<Permission?> SaveAsync(Permission m, CancellationToken ct = default) => _repo.SaveAsync(m, ct);
        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) => _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
