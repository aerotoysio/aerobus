using AeroBus.Core.Data;
using AeroBus.Core.Model.Stock;

namespace AeroBus.Core.Repositories.Stock
{
    public interface IProductCounters
    {
        Task<ProductCounter?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<ProductCounter>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<ProductCounter?> GetBySkuBucketAsync(Guid companyId, string sku, string bucket, CancellationToken ct = default);
        Task<ProductCounter?> SaveAsync(ProductCounter model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class ProductCounters(IDocumentStore store) : DocumentRepository<ProductCounter>(store), IProductCounters
    {
        protected override string Collection => DfCollections.Stock.ProductCounters;

        // GetById / GetByCompany / Save come from DocumentRepository<ProductCounter>.

        public async Task<ProductCounter?> GetBySkuBucketAsync(Guid companyId, string sku, string bucket, CancellationToken ct = default)
        {
            var matches = await QueryAsync(new Dictionary<string, object?>
            {
                ["companyId"] = companyId,
                ["sku"] = sku,
                ["bucket"] = bucket,
            }, ct: ct);
            return matches.Count > 0 ? matches[0] : null;
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
