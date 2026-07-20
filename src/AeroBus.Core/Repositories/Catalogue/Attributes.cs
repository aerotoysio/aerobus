using AeroBus.Core.Data;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IAttributes
    {
        Task<Model.Catalogue.Attribute?> GetByIdAsync(Guid id, Guid? companyId = null, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Catalogue.Attribute>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Catalogue.Attribute>> GetByParentAsync(Guid parentId, Guid? companyId = null, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Catalogue.Attribute>> SearchAsync(Guid companyId, string? search = null, CancellationToken ct = default);

        Task<Model.Catalogue.Attribute?> SaveAsync(Model.Catalogue.Attribute model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid? companyId = null, Guid? concurrencyId = null, CancellationToken ct = default);
    }

    public sealed class AttributesRepo(IDocumentStore store) : DocumentRepository<Model.Catalogue.Attribute>(store), IAttributes
    {
        protected override string Collection => DfCollections.Catalogue.Attributes;

        public Task<Model.Catalogue.Attribute?> GetByIdAsync(Guid id, Guid? companyId = null, CancellationToken ct = default) =>
            base.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Model.Catalogue.Attribute>> GetByParentAsync(Guid parentId, Guid? companyId = null, CancellationToken ct = default) =>
            QueryAsync(Eq("parentId", parentId), ct: ct);

        public Task<IReadOnlyList<Model.Catalogue.Attribute>> SearchAsync(Guid companyId, string? search = null, CancellationToken ct = default) =>
            QueryAsync(Eq("companyId", companyId), ct: ct);

        public Task<bool> DeleteAsync(Guid id, Guid? companyId = null, Guid? concurrencyId = null, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
