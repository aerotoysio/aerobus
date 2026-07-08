using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class RegionsService(IRegions repo)
    {
        private readonly IRegions _repo = repo;

        public Task<Region?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Region>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Region>> GetByCountryAsync(Guid countryId, CancellationToken ct = default) =>
            _repo.GetByCountryAsync(countryId, ct);

        public Task<IReadOnlyList<Region>> ListByCompanyAsync(
            Guid companyId, Guid? countryId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, countryId, status, search, pageNumber, pageSize, ct);

        public Task<Region?> SaveAsync(Region model, CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
