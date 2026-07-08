using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class SchedulesService(ISchedules repo)
    {
        private readonly ISchedules _repo = repo;

        public Task<Schedule?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Schedule>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Schedule>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? carrierCode,
            string? departureStation,
            string? arrivalStation,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(
                companyId,
                status,
                carrierCode,
                departureStation,
                arrivalStation,
                search,
                pageNumber,
                pageSize,
                ct);

        public Task<Schedule?> SaveAsync(
            Schedule model,
            CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
