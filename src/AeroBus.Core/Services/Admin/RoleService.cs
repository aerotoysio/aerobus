using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    public sealed class RoleService(IRoles repo)
    {
        private readonly IRoles _repo = repo;

        public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default) => _repo.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<Role>> GetByCompanyAsync(Guid? companyId, CancellationToken ct = default)
            => _repo.GetByCompanyAsync(companyId, ct);
        public Task<Role?> SaveAsync(Role m, CancellationToken ct = default) => _repo.SaveAsync(m, ct);
        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) => _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
