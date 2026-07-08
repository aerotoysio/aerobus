using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    public sealed class CompanyService(ICompanies repo)
    {
        private readonly ICompanies _repo = repo;

        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) => _repo.GetByIdAsync(id, ct);
        public Task<Company?> GetBySlugAsync(string slug, CancellationToken ct = default) => _repo.GetBySlugAsync(slug, ct);
        public Task<IReadOnlyList<Company>> GetAllAsync(CancellationToken ct = default) => _repo.GetAllAsync(ct);
        public Task<Company?> SaveAsync(Company m, CancellationToken ct = default) => _repo.SaveAsync(m, ct);
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);
    }
}
