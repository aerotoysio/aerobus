using AeroBus.Core.Data;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IEnvironments
    {
        Task<Model.Admin.Environment?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Admin.Environment>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Admin.Environment>> ListAsync(Guid? companyId = null, CancellationToken ct = default);
        Task<Model.Admin.Environment?> SaveAsync(Model.Admin.Environment m, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid ConcurrencyId, CancellationToken ct = default);
    }

    public sealed class Environments(IDocumentStore store) : DocumentRepository<Model.Admin.Environment>(store), IEnvironments
    {
        protected override string Collection => DfCollections.Admin.Environments;

        public Task<IReadOnlyList<Model.Admin.Environment>> ListAsync(Guid? companyId = null, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?>();
            if (companyId is { } cid) f["CompanyId"] = cid;
            return QueryAsync(f, ct: ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ConcurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
