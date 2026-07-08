using AeroBus.Core.Model;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class FlightsService(IFlights repo)
    {
        private readonly IFlights _repo = repo;

        public Task<Flight?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Flight>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Flight>> LoadByCompanyAsync(
            Guid companyId,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.LoadByCompanyAsync(companyId, pageNumber, pageSize, ct);

        public Task<IReadOnlyList<Flight>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? departureStation,
            string? arrivalStation,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, status, departureStation, arrivalStation, search, pageNumber, pageSize, ct);

        public Task<PagedResult<Flight>> ListByCompanyPagedAsync(
            Guid companyId,
            string? status,
            string? departureStation,
            string? arrivalStation,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyPagedAsync(companyId, status, departureStation, arrivalStation, search, pageNumber, pageSize, ct);

        public Task<IReadOnlyList<Flight>> FindByLocalRangeAsync(
            Guid companyId,
            string departureStation,
            string arrivalStation,
            DateTime fromLocal,
            DateTime toLocal,
            CancellationToken ct = default) =>
            _repo.FindByLocalRangeAsync(companyId, departureStation, arrivalStation, fromLocal, toLocal, ct);

        public Task<IReadOnlyList<Flight>> FindByUtcRangeAsync(
            Guid companyId,
            string departureStation,
            string arrivalStation,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default) =>
            _repo.FindByUtcRangeAsync(companyId, departureStation, arrivalStation, fromUtc, toUtc, ct);

        public Task<Flight?> SaveAsync(
            Flight model,
            CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
