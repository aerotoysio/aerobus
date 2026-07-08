using AeroBus.Core.Events;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class ProductsService(IProducts repo, IEventPublisher events)
    {
        private readonly IProducts _repo = repo;
        private readonly IEventPublisher _events = events;

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Product>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Product>> ListByCompanyAsync(
            Guid companyId, string? category, string? productType, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, category, productType, status, search, pageNumber, pageSize, ct);

        public async Task<Product?> SaveAsync(Product model, CancellationToken ct = default)
        {
            var saved = await _repo.SaveAsync(model, ct);
            var p = saved ?? model;
            await _events.PublishAsync("product.changed",
                new EventSubject("products", p.Id.ToString()),
                new { id = p.Id, name = p.Name, status = p.Status },
                p.CompanyId, actor: "products", ct);
            return saved;
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
