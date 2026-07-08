using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class ContinentsService(IContinents repo)
    {
        private readonly IContinents _repo = repo;

        public Task<Continent?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Continent>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Continent>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, search, pageNumber, pageSize, ct);

        public Task<Continent?> SaveAsync(Continent model, CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
