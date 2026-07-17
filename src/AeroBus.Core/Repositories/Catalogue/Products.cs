using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IProducts
    {
        Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Product>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Product>> ListByCompanyAsync(
            Guid companyId, string? category, string? productType, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Product?> SaveAsync(Product model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Products(IDocumentStore store) : DocumentRepository<Product>(store), IProducts
    {
        protected override string Collection => DfCollections.Catalogue.Products;

        // Product metadata (custom fields) is embedded in the Product document,
        // so saving/loading a product carries its metadata in one round trip.

        public Task<IReadOnlyList<Product>> ListByCompanyAsync(
            Guid companyId, string? category, string? productType, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["CompanyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(category)) f["Category"] = category;
            if (!string.IsNullOrWhiteSpace(productType)) f["ProductType"] = productType;
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            return QueryAsync(f, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
