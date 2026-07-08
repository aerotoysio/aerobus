using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class AirportService(IAirports repo)
    {
        private readonly IAirports _repo = repo;

        // TO-DO: read from the cache, not the DB...
        public Task<Airport?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Airport>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Airport>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, search, pageNumber, pageSize, ct);

        public Task<Airport?> SaveAsync(Airport model, CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
