using AeroBus.Core.Events;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Model.Operations;
using AeroBus.Core.Repositories.Operations;
using AeroBus.Core.Repositories.Order;
using AeroBus.Core.Services.Distribution;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Operations
{
    /// <summary>Outcome of a check-in / board action. <see cref="Code"/> is a stable
    /// machine code (<c>ok</c> / <c>notFound</c> / <c>invalidState</c>).</summary>
    public sealed record CheckInResult(bool Success, string Code, string? Message, PassengerCheckIn? CheckIn)
    {
        public static CheckInResult Ok(PassengerCheckIn c) => new(true, "ok", null, c);
        public static CheckInResult NotFound() => new(false, "notFound", "No manifest entry for that passenger on this flight.", null);
        public static CheckInResult Invalid(string message, PassengerCheckIn c) => new(false, "invalidState", message, c);
    }

    /// <summary>
    /// The passenger side of departure control: read a flight's manifest and advance
    /// each passenger Booked → CheckedIn → Boarded. The per-passenger row is the
    /// operational source of truth; each transition emits an event and best-effort
    /// rolls the owning order's lifecycle forward through the existing
    /// <see cref="OrderChangeService"/> once every passenger on that order+flight has
    /// reached the state (so the order document stays the authoritative commercial
    /// record).
    ///
    /// Per-passenger flips are single-writer in practice (one agent works one
    /// passenger), so this uses a straightforward load-check-save rather than the
    /// conditional-update CAS the high-contention seat inventory needs.
    /// </summary>
    public sealed class CheckInService
    {
        private readonly ICheckIns _checkIns;
        private readonly IOrders _orders;
        private readonly OrderChangeService _orderChange;
        private readonly IEventPublisher _events;
        private readonly ILogger<CheckInService> _log;

        public CheckInService(
            ICheckIns checkIns, IOrders orders, OrderChangeService orderChange,
            IEventPublisher events, ILogger<CheckInService> log)
        {
            _checkIns = checkIns;
            _orders = orders;
            _orderChange = orderChange;
            _events = events;
            _log = log;
        }

        public async Task<IReadOnlyList<PassengerCheckIn>> GetManifestAsync(
            Guid companyId, Guid flightId, CancellationToken ct = default)
        {
            var rows = await _checkIns.GetByFlightAsync(flightId, ct);
            return rows.Where(r => r.CompanyId == companyId)
                       .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                       .ToList();
        }

        public async Task<CheckInResult> CheckInAsync(
            Guid companyId, Guid flightId, Guid passengerId, int? seatRow, string? seatColumn, CancellationToken ct = default)
        {
            var row = await LoadAsync(companyId, flightId, passengerId, ct);
            if (row is null) return CheckInResult.NotFound();
            if (row.Status == CheckInStatus.Boarded)
                return CheckInResult.Invalid("Passenger has already boarded.", row);

            var already = row.Status == CheckInStatus.CheckedIn;
            row.Status = CheckInStatus.CheckedIn;
            row.CheckedInAt ??= DateTime.UtcNow;
            if (seatRow is not null) row.SeatRow = seatRow;
            if (!string.IsNullOrWhiteSpace(seatColumn)) row.SeatColumn = seatColumn;
            row.Updated = DateTime.UtcNow;
            var saved = await _checkIns.SaveAsync(row, ct) ?? row;

            if (!already)
            {
                await _events.PublishAsync("checkin.completed",
                    new EventSubject("checkins", saved.Id.ToString()),
                    new { id = saved.Id, flightId, passengerId, seat = Seat(saved) },
                    companyId, actor: "check-in", ct);
                await RollUpOrderAsync(companyId, saved, CheckInStatus.CheckedIn, OrderStateMachine.Action.CheckIn, ct);
            }
            return CheckInResult.Ok(saved);
        }

        public async Task<CheckInResult> BoardAsync(
            Guid companyId, Guid flightId, Guid passengerId, CancellationToken ct = default)
        {
            var row = await LoadAsync(companyId, flightId, passengerId, ct);
            if (row is null) return CheckInResult.NotFound();
            if (row.Status == CheckInStatus.Booked)
                return CheckInResult.Invalid("Passenger must check in before boarding.", row);
            if (row.Status == CheckInStatus.Boarded)
                return CheckInResult.Ok(row); // idempotent

            row.Status = CheckInStatus.Boarded;
            row.BoardedAt ??= DateTime.UtcNow;
            row.BoardingSequence ??= await NextBoardingSequenceAsync(flightId, ct);
            row.Updated = DateTime.UtcNow;
            var saved = await _checkIns.SaveAsync(row, ct) ?? row;

            await _events.PublishAsync("passenger.boarded",
                new EventSubject("checkins", saved.Id.ToString()),
                new { id = saved.Id, flightId, passengerId, sequence = saved.BoardingSequence, seat = Seat(saved) },
                companyId, actor: "boarding", ct);
            await RollUpOrderAsync(companyId, saved, CheckInStatus.Boarded, OrderStateMachine.Action.Board, ct);
            return CheckInResult.Ok(saved);
        }

        /// <summary>Board every checked-in passenger on the flight. Returns how many boarded.</summary>
        public async Task<int> BoardAllAsync(Guid companyId, Guid flightId, CancellationToken ct = default)
        {
            var manifest = await GetManifestAsync(companyId, flightId, ct);
            var boarded = 0;
            foreach (var row in manifest.Where(r => r.Status == CheckInStatus.CheckedIn))
            {
                var result = await BoardAsync(companyId, flightId, row.PassengerId, ct);
                if (result.Success) boarded++;
            }
            return boarded;
        }

        private async Task<PassengerCheckIn?> LoadAsync(Guid companyId, Guid flightId, Guid passengerId, CancellationToken ct)
        {
            var row = await _checkIns.GetByFlightAndPassengerAsync(flightId, passengerId, ct);
            return row is not null && row.CompanyId == companyId ? row : null;
        }

        private async Task<int> NextBoardingSequenceAsync(Guid flightId, CancellationToken ct)
        {
            var manifest = await _checkIns.GetByFlightAsync(flightId, ct);
            return manifest.Count(r => r.BoardingSequence is not null) + 1;
        }

        /// <summary>Best-effort: once every passenger on this order+flight has reached
        /// <paramref name="reached"/>, advance the order lifecycle so the commercial
        /// record tracks operations. A failed/blocked transition is fine — the
        /// per-passenger rows remain authoritative.</summary>
        private async Task RollUpOrderAsync(Guid companyId, PassengerCheckIn row, string reached, string action, CancellationToken ct)
        {
            try
            {
                var siblings = await _checkIns.GetByOrderAndFlightAsync(row.OrderId, row.FlightId, ct);
                if (siblings.Count == 0 || !siblings.All(s => AtLeast(s.Status, reached))) return;

                var response = await _orderChange.ChangeStatus(
                    new OrderChangeRequest { OrderId = row.OrderId, Action = action, Reason = "DCS roll-up" }, companyId, ct: ct);
                if (!response.Success)
                    _log.LogDebug("Order {OrderId} roll-up '{Action}' not applied: {Error}", row.OrderId, action, response.ErrorMessage);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Order {OrderId} lifecycle roll-up '{Action}' failed (per-passenger state stands).", row.OrderId, action);
            }
        }

        private static bool AtLeast(string status, string target)
        {
            int Rank(string s) => s switch
            {
                CheckInStatus.Booked => 0,
                CheckInStatus.CheckedIn => 1,
                CheckInStatus.Boarded => 2,
                _ => 0,
            };
            return Rank(status) >= Rank(target);
        }

        private static string? Seat(PassengerCheckIn c) =>
            c.SeatRow is { } r ? $"{r}{c.SeatColumn}" : null;
    }
}
