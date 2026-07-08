using AeroBus.Core.Events;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class BundleService(IBundles repo, IEventPublisher events)
    {
        private readonly IBundles _repo = repo;
        private readonly IEventPublisher _events = events;

        public Task<Bundle?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Bundle>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Bundle>> GetPrettyByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetPrettyByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Bundle>> SearchAsync(
            Guid? companyId = null,
            string? search = null,
            string? status = null,
            string? type = null,
            string? category = null,
            int pageNumber = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
            _repo.SearchAsync(companyId, search, status, type, category, pageNumber, pageSize, ct);

        public async Task<Bundle?> SaveAsync(Bundle model, CancellationToken ct = default)
        {
            var saved = await _repo.SaveAsync(model, ct);
            var b = saved ?? model;
            await _events.PublishAsync("bundle.changed",
                new EventSubject("bundles", b.Id.ToString()),
                new { id = b.Id, name = b.Name, status = b.Status },
                b.CompanyId, actor: "bundles", ct);
            return saved;
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
