using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    public sealed class RolePermissionsService(IRolePermissions repo)
    {
        private readonly IRolePermissions _repo = repo;

        public Task<bool> AddRolePermissionAsync(Guid roleId, Guid permissionsId, CancellationToken ct = default)
            => _repo.AddRolePermissionAsync(roleId, permissionsId, ct);

        public Task<bool> RemoveRolePermissionAsync(Guid roleId, Guid permissionsId, CancellationToken ct = default)
            => _repo.RemoveRolePermissionAsync(roleId, permissionsId, ct);

        public Task<IReadOnlyList<RolePermission>> GetPermissionsForRoleAsync(Guid roleId, CancellationToken ct = default)
            => _repo.GetPermissionsForRoleAsync(roleId, ct);
    }
}
