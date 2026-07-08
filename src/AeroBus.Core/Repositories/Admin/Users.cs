using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IUsers
    {
        Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<User?> GetByEmailAsync(string email, string companySlug, CancellationToken ct = default);
        Task<IReadOnlyList<User>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Permission>> GetPermissionsByUserIdAsync(Guid id, CancellationToken ct = default);
        Task<User?> SaveAsync(User model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public sealed class Users(IDocumentStore store) : IUsers
    {
        private readonly IDocumentStore _store = store;
        private const string C = "users";

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<User>(C, id, ct);

        public Task<User?> GetByEmailAsync(string email, string companySlug, CancellationToken ct = default) =>
            _store.GetByFieldAsync<User>(C, "Email", email, ct);

        public Task<IReadOnlyList<User>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<User>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, ct: ct);

        // Resolve a user's permissions through their role's embedded PermissionIds.
        public async Task<IReadOnlyList<Permission>> GetPermissionsByUserIdAsync(Guid id, CancellationToken ct = default)
        {
            var user = await _store.GetByIdAsync<User>(C, id, ct);
            if (user?.RoleId is not { } roleId) return Array.Empty<Permission>();

            var role = await _store.GetByIdAsync<Role>("roles", roleId, ct);
            if (role?.PermissionIds is not { Count: > 0 } pids) return Array.Empty<Permission>();

            var perms = new List<Permission>(pids.Count);
            foreach (var pid in pids)
            {
                var p = await _store.GetByIdAsync<Permission>("permissions", pid, ct);
                if (p is not null) perms.Add(p);
            }
            return perms;
        }

        public async Task<User?> SaveAsync(User m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
