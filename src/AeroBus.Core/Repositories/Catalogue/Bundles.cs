using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IBundles
    {
        Task<Bundle?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Bundle>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Bundle>> GetPrettyByCompanyAsync(Guid companyId, CancellationToken ct = default);

        Task<IReadOnlyList<Bundle>> SearchAsync(
            Guid? companyId = null,
            string? search = null,
            string? status = null,
            string? type = null,
            string? category = null,
            int pageNumber = 1,
            int pageSize = 50,
            CancellationToken ct = default);

        Task<Bundle?> SaveAsync(Bundle model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Bundles(IDocumentStore store) : DocumentRepository<Bundle>(store), IBundles
    {
        protected override string Collection => DfCollections.Catalogue.Bundles;

        public Task<IReadOnlyList<Bundle>> GetPrettyByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            QueryAsync(Eq(Df.Field(nameof(Bundle.CompanyId)), companyId), ct: ct);

        public Task<IReadOnlyList<Bundle>> SearchAsync(
            Guid? companyId = null,
            string? search = null,
            string? status = null,
            string? type = null,
            string? category = null,
            int pageNumber = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?>();
            if (companyId is { } cid) f[Df.Field(nameof(Bundle.CompanyId))] = cid;
            if (!string.IsNullOrWhiteSpace(status)) f[Df.Field(nameof(Bundle.Status))] = status;
            if (!string.IsNullOrWhiteSpace(type)) f[Df.Field(nameof(Bundle.Type))] = type;
            if (!string.IsNullOrWhiteSpace(category)) f[Df.Field(nameof(Bundle.Category))] = category;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
