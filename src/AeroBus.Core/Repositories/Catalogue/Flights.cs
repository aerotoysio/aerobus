using System.Globalization;
using AeroBus.Core.Data;
using AeroBus.Core.Model;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IFlights
    {
        Task<Flight?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> GetByScheduleIdAsync(Guid scheduleId, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> ListByCompanyAsync(
            Guid companyId, string? status, string? departureStation, string? arrivalStation,
            string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> LoadByCompanyAsync(Guid companyId, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> GetByFullRangeAsync(
            Guid companyId, string? status, DateTime fromUtc, DateTime toUtc, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> FindByLocalRangeAsync(
            Guid companyId, string departureStation, string arrivalStation, DateTime fromLocal, DateTime toLocal, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> FindByUtcRangeAsync(
            Guid companyId, string departureStation, string arrivalStation, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
        Task<PagedResult<Flight>> ListByCompanyPagedAsync(
            Guid companyId, string? status, string? departureStation, string? arrivalStation,
            string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Flight?> SaveAsync(Flight model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    // Flight is not an IDocument aggregate marker type (kept faithful to ooms),
    // so this repository talks to IDocumentStore directly rather than through
    // DocumentRepository<T>.
    public sealed class Flights(IDocumentStore store) : IFlights
    {
        private readonly IDocumentStore _store = store;
        private const string C = "flights";

        private static string D(DateTime dt) => dt.ToString("o", CultureInfo.InvariantCulture);

        public Task<Flight?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Flight>(C, id, ct);

        public Task<IReadOnlyList<Flight>> GetByScheduleIdAsync(Guid scheduleId, CancellationToken ct = default) =>
            _store.QueryAsync<Flight>(C, new Dictionary<string, object?> { ["ScheduleId"] = scheduleId }, ct: ct);

        public Task<IReadOnlyList<Flight>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Flight>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, ct: ct);

        public Task<IReadOnlyList<Flight>> LoadByCompanyAsync(Guid companyId, int pageNumber, int pageSize, CancellationToken ct = default) =>
            _store.QueryAsync<Flight>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, pageNumber, pageSize, ct);

        public Task<IReadOnlyList<Flight>> ListByCompanyAsync(
            Guid companyId, string? status, string? departureStation, string? arrivalStation,
            string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["CompanyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            if (!string.IsNullOrWhiteSpace(departureStation)) f["DepartureStation"] = departureStation;
            if (!string.IsNullOrWhiteSpace(arrivalStation)) f["ArrivalStation"] = arrivalStation;
            return _store.QueryAsync<Flight>(C, f, pageNumber, pageSize, ct);
        }

        public Task<IReadOnlyList<Flight>> GetByFullRangeAsync(
            Guid companyId, string? status, DateTime fromUtc, DateTime toUtc, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var where = $"CompanyId = '{companyId}' AND DepartureDateTime >= '{D(fromUtc)}' AND DepartureDateTime <= '{D(toUtc)}'";
            if (!string.IsNullOrWhiteSpace(status)) where += $" AND Status = '{status.Replace("'", "''")}'";
            return _store.QueryWhereAsync<Flight>(C, where, pageNumber, pageSize, ct);
        }

        public Task<IReadOnlyList<Flight>> FindByLocalRangeAsync(
            Guid companyId, string departureStation, string arrivalStation, DateTime fromLocal, DateTime toLocal, CancellationToken ct = default)
        {
            var where = $"CompanyId = '{companyId}' AND DepartureStation = '{departureStation.Replace("'", "''")}' AND ArrivalStation = '{arrivalStation.Replace("'", "''")}' AND DepartureDateTimeLocal >= '{D(fromLocal)}' AND DepartureDateTimeLocal <= '{D(toLocal)}'";
            return _store.QueryWhereAsync<Flight>(C, where, ct: ct);
        }

        public Task<IReadOnlyList<Flight>> FindByUtcRangeAsync(
            Guid companyId, string departureStation, string arrivalStation, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            var where = $"CompanyId = '{companyId}' AND DepartureStation = '{departureStation.Replace("'", "''")}' AND ArrivalStation = '{arrivalStation.Replace("'", "''")}' AND DepartureDateTime >= '{D(fromUtc)}' AND DepartureDateTime <= '{D(toUtc)}'";
            return _store.QueryWhereAsync<Flight>(C, where, ct: ct);
        }

        public async Task<PagedResult<Flight>> ListByCompanyPagedAsync(
            Guid companyId, string? status, string? departureStation, string? arrivalStation,
            string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["CompanyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            if (!string.IsNullOrWhiteSpace(departureStation)) f["DepartureStation"] = departureStation;
            if (!string.IsNullOrWhiteSpace(arrivalStation)) f["ArrivalStation"] = arrivalStation;
            var items = await _store.QueryAsync<Flight>(C, f, pageNumber, pageSize, ct);
            var total = await _store.CountAsync(C, f, ct);
            return new PagedResult<Flight> { Items = items, TotalCount = total, PageNumber = pageNumber, PageSize = pageSize };
        }

        public async Task<Flight?> SaveAsync(Flight m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
