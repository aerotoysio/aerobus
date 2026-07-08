using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class MediaService(IMedia repo)
    {
        private readonly IMedia _repo = repo;

        public Task<Media?> GetByIdAsync(Guid id, Guid? companyId = null, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, companyId, ct);

        public Task<IReadOnlyList<Media>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Media>> GetByParentAsync(Guid parentId, Guid? companyId = null, CancellationToken ct = default) =>
            _repo.GetByParentAsync(parentId, companyId, ct);

        public Task<IReadOnlyList<Media>> SearchAsync(Guid companyId, string? search = null, CancellationToken ct = default) =>
            _repo.SearchAsync(companyId, search, ct);

        public Task<Media?> SaveAsync(Media m, CancellationToken ct = default) =>
            _repo.SaveAsync(m, ct);

        public Task<bool> DeleteAsync(Guid id, Guid? companyId = null, Guid? concurrencyId = null, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, companyId, concurrencyId, ct);
    }
}
