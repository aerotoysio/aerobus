using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IRoles
    {
        Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Role>> GetByCompanyAsync(Guid? companyId, CancellationToken ct = default);
        Task<Role?> SaveAsync(Role m, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Roles(IDocumentStore store) : IRoles
    {
        private readonly IDocumentStore _store = store;
        private const string C = "roles";

        public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Role>(C, id, ct);

        public Task<IReadOnlyList<Role>> GetByCompanyAsync(Guid? companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Role>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, ct: ct);

        public async Task<Role?> SaveAsync(Role m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
