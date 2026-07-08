using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IMedia
    {
        Task<Media?> GetByIdAsync(Guid id, Guid? companyId = null, CancellationToken ct = default);
        Task<IReadOnlyList<Media>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Media>> GetByParentAsync(Guid parentId, Guid? companyId = null, CancellationToken ct = default);
        Task<IReadOnlyList<Media>> SearchAsync(Guid companyId, string? search = null, CancellationToken ct = default);

        Task<Media?> SaveAsync(Media model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid? companyId = null, Guid? concurrencyId = null, CancellationToken ct = default);
    }

    public sealed class MediaRepo(IDocumentStore store) : DocumentRepository<Media>(store), IMedia
    {
        protected override string Collection => "media";

        public Task<Media?> GetByIdAsync(Guid id, Guid? companyId = null, CancellationToken ct = default) =>
            base.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Media>> GetByParentAsync(Guid parentId, Guid? companyId = null, CancellationToken ct = default) =>
            QueryAsync(Eq("ParentId", parentId), ct: ct);

        public Task<IReadOnlyList<Media>> SearchAsync(Guid companyId, string? search = null, CancellationToken ct = default) =>
            QueryAsync(Eq("CompanyId", companyId), ct: ct);

        public Task<bool> DeleteAsync(Guid id, Guid? companyId = null, Guid? concurrencyId = null, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
