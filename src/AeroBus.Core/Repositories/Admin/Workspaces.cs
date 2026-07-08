using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IWorkspaces
    {
        Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Workspace>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<Workspace?> SaveAsync(Workspace m, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid ConcurrencyId, CancellationToken ct = default);
    }

    public sealed class Workspaces(IDocumentStore store) : DocumentRepository<Workspace>(store), IWorkspaces
    {
        protected override string Collection => "workspaces";

        public Task<bool> DeleteAsync(Guid id, Guid ConcurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
