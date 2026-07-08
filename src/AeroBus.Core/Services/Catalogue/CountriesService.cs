using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class CountriesService(ICountries repo)
    {
        private readonly ICountries _repo = repo;

        public Task<Country?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Country>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Country>> GetByContinentAsync(Guid continentId, CancellationToken ct = default) =>
            _repo.GetByContinentAsync(continentId, ct);

        public Task<IReadOnlyList<Country>> ListByCompanyAsync(
            Guid companyId, Guid? continentId, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, continentId, status, search, pageNumber, pageSize, ct);

        public Task<Country?> SaveAsync(Country model, CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
