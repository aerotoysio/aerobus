using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IPermissions
    {
        Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Permission>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<Permission?> SaveAsync(Permission m, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Permissions(IDocumentStore store) : IPermissions
    {
        private readonly IDocumentStore _store = store;
        private const string C = "permissions";

        public Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Permission>(C, id, ct);

        public Task<IReadOnlyList<Permission>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Permission>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, ct: ct);

        public async Task<Permission?> SaveAsync(Permission m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
