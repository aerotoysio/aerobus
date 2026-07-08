using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    // RolePermissions are folded into the Role document (Role.PermissionIds).
    // This facade keeps the existing interface but mutates the role doc.
    public interface IRolePermissions
    {
        Task<bool> AddRolePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default);
        Task<IReadOnlyList<RolePermission>> GetPermissionsForRoleAsync(Guid roleId, CancellationToken ct = default);
        Task<bool> RemoveRolePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default);
    }

    public sealed class RolePermissions(IDocumentStore store) : IRolePermissions
    {
        private readonly IDocumentStore _store = store;
        private const string C = "roles";

        public async Task<bool> AddRolePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
        {
            var role = await _store.GetByIdAsync<Role>(C, roleId, ct);
            if (role is null) return false;
            var ids = role.PermissionIds ?? new List<Guid>();
            if (!ids.Contains(permissionId)) ids.Add(permissionId);
            await _store.UpsertAsync(C, role with { PermissionIds = ids }, role.Id, ct);
            return true;
        }

        public async Task<bool> RemoveRolePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
        {
            var role = await _store.GetByIdAsync<Role>(C, roleId, ct);
            if (role?.PermissionIds is not { } ids) return false;
            ids.Remove(permissionId);
            await _store.UpsertAsync(C, role with { PermissionIds = ids }, role.Id, ct);
            return true;
        }

        public async Task<IReadOnlyList<RolePermission>> GetPermissionsForRoleAsync(Guid roleId, CancellationToken ct = default)
        {
            var role = await _store.GetByIdAsync<Role>(C, roleId, ct);
            if (role?.PermissionIds is not { } ids) return Array.Empty<RolePermission>();
            return ids.Select(pid => new RolePermission { RoleId = roleId, PermissionId = pid }).ToList();
        }
    }
}
