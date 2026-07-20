using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface ISchedules
    {
        Task<Schedule?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Schedule>> GetByGroupingIdAsync(Guid groupingId, CancellationToken ct = default);
        Task<Schedule?> GetPreviousByGroupingIdAsync(Guid groupingId, Guid currentScheduleId, CancellationToken ct = default);
        Task<IReadOnlyList<Schedule>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Schedule>> GetAllByCompanyAsync(Guid companyId, string status, CancellationToken ct = default);
        Task<IReadOnlyList<Schedule>> ListByCompanyAsync(
            Guid companyId, string? status, string? carrierCode, string? departureStation, string? arrivalStation,
            string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Schedule?> SaveAsync(Schedule model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    // Schedule is not an IDocument marker type (kept faithful to ooms), so this
    // repository talks to IDocumentStore directly.
    public sealed class Schedules(IDocumentStore store) : ISchedules
    {
        private readonly IDocumentStore _store = store;
        private const string C = DfCollections.Catalogue.Schedules;

        public Task<Schedule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Schedule>(C, id, ct);

        public Task<IReadOnlyList<Schedule>> GetByGroupingIdAsync(Guid groupingId, CancellationToken ct = default) =>
            _store.QueryAsync<Schedule>(C, new Dictionary<string, object?> { ["groupingId"] = groupingId }, ct: ct);

        public async Task<Schedule?> GetPreviousByGroupingIdAsync(Guid groupingId, Guid currentScheduleId, CancellationToken ct = default)
        {
            var all = await _store.QueryAsync<Schedule>(C, new Dictionary<string, object?> { ["groupingId"] = groupingId }, ct: ct);
            return all.Where(s => s.Id != currentScheduleId).OrderByDescending(s => s.Created).FirstOrDefault();
        }

        public Task<IReadOnlyList<Schedule>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Schedule>(C, new Dictionary<string, object?> { ["companyId"] = companyId }, ct: ct);

        public Task<IReadOnlyList<Schedule>> GetAllByCompanyAsync(Guid companyId, string status, CancellationToken ct = default) =>
            _store.QueryAsync<Schedule>(C, new Dictionary<string, object?> { ["companyId"] = companyId, ["status"] = status }, ct: ct);

        public Task<IReadOnlyList<Schedule>> ListByCompanyAsync(
            Guid companyId, string? status, string? carrierCode, string? departureStation, string? arrivalStation,
            string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["companyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(status)) f["status"] = status;
            if (!string.IsNullOrWhiteSpace(carrierCode)) f["carrierCode"] = carrierCode;
            if (!string.IsNullOrWhiteSpace(departureStation)) f["departureStation"] = departureStation;
            if (!string.IsNullOrWhiteSpace(arrivalStation)) f["arrivalStation"] = arrivalStation;
            return _store.QueryAsync<Schedule>(C, f, pageNumber, pageSize, ct);
        }

        public async Task<Schedule?> SaveAsync(Schedule m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
