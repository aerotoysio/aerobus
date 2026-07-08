using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    public sealed class CompanyConfigService(ICompanyConfigs repo)
    {
        private readonly ICompanyConfigs _repo = repo;
        public Task<CompanyConfig?> GetByIdAsync(Guid id, CancellationToken ct = default) => _repo.GetByIdAsync(id, null!, ct);
        public Task<IReadOnlyList<CompanyConfig>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
            => _repo.GetByCompanyAsync(companyId, ct);
        public Task<CompanyConfig?> SaveAsync(CompanyConfig m, CancellationToken ct = default) => _repo.SaveAsync(m, ct);
        public Task<bool> DeleteAsync(Guid companyId, string key, CancellationToken ct = default) => _repo.DeleteAsync(companyId, key, ct);
    }
}
