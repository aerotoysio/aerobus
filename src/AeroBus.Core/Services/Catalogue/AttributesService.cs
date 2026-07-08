using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class AttributesService(IAttributes repo)
    {
        private readonly IAttributes _repo = repo;

        public Task<Model.Catalogue.Attribute?> GetByIdAsync(Guid id, Guid? companyId = null, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, companyId, ct);

        public Task<IReadOnlyList<Model.Catalogue.Attribute>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Model.Catalogue.Attribute>> GetByParentAsync(Guid parentId, Guid? companyId = null, CancellationToken ct = default) =>
            _repo.GetByParentAsync(parentId, companyId, ct);

        public Task<IReadOnlyList<Model.Catalogue.Attribute>> SearchAsync(Guid companyId, string? search = null, CancellationToken ct = default) =>
            _repo.SearchAsync(companyId, search, ct);

        public Task<Model.Catalogue.Attribute?> SaveAsync(Model.Catalogue.Attribute m, CancellationToken ct = default) =>
            _repo.SaveAsync(m, ct);

        public Task<bool> DeleteAsync(Guid id, Guid? companyId = null, Guid? concurrencyId = null, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, companyId, concurrencyId, ct);
    }
}
