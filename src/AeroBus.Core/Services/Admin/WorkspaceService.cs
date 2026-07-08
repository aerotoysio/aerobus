using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    /// <summary>
    /// Workspace CRUD over the document store. The ooms original also
    /// bootstrapped a Git branch per workspace and supported promote /
    /// ensure-branch / list-files flows via GitHub; those were dropped in the
    /// AeroBus port (no GitHub integration) — the corresponding endpoints
    /// return 501.
    /// </summary>
    public sealed class WorkspaceService(IWorkspaces repo, ICompanies companies)
    {
        public enum WorkspaceBranchCleanup
        {
            Keep,           // leave branch as-is
            Delete,         // delete branch outright
            TagAndDelete    // create tag then delete branch (default for unpromoted workspaces)
        }

        private readonly IWorkspaces _repo = repo;
        private readonly ICompanies _companies = companies;

        public Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Workspace>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
            => _repo.GetByCompanyAsync(companyId, ct);

        /// <summary>
        /// Saves the workspace. The company must exist (same validation as the
        /// ooms original). Git branch bootstrap for new workspaces was dropped.
        /// </summary>
        public async Task<Workspace?> SaveAsync(Workspace ws, CancellationToken ct = default)
        {
            _ = await _companies.GetByIdAsync(ws.CompanyId, ct)
                ?? throw new InvalidOperationException($"Company {ws.CompanyId} not found.");

            return await _repo.SaveAsync(ws, ct);
        }

        /// <summary>
        /// Deletes the workspace record. The branch-cleanup policy is accepted
        /// for wire compatibility but ignored — there is no Git backing here.
        /// </summary>
        public async Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default, WorkspaceBranchCleanup? overrideCleanup = null)
        {
            var ws = await _repo.GetByIdAsync(id, ct);
            if (ws is null) return false;

            return await _repo.DeleteAsync(id, concurrencyId, ct);
        }
    }
}
