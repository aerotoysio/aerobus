using AeroBus.Core.Events;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Repositories.Stock;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Operations
{
    /// <summary>
    /// Outcome of a flight operational action. <see cref="Success"/> is the branch;
    /// <see cref="Code"/> is a stable machine code (<c>ok</c> / <c>notFound</c> /
    /// <c>invalidTransition</c>) an endpoint maps to a status.
    /// </summary>
    public sealed record FlightOpResult(
        bool Success, string Code, string? Message, Flight? Flight, IReadOnlyList<string>? AvailableActions = null)
    {
        public static FlightOpResult Ok(Flight flight) => new(true, "ok", null, flight);
        public static FlightOpResult NotFound() => new(false, "notFound", "Flight not found.", null);
        public static FlightOpResult Invalid(string message, IReadOnlyList<string> actions) =>
            new(false, "invalidTransition", message, null, actions);
    }

    /// <summary>
    /// The flight side of departure control: list a station's departures for a day
    /// and drive the flight operational status lifecycle
    /// (Scheduled → Boarding → Departed / Cancelled) through
    /// <see cref="FlightStateMachine"/>. Status changes emit operational events via
    /// the outbox, mirroring how the flight builder emits <c>flight.built</c>.
    /// </summary>
    public sealed class FlightOperationsService
    {
        private readonly IFlights _flights;
        private readonly IFlightInventories _inventories;
        private readonly IEventPublisher _events;
        private readonly ILogger<FlightOperationsService> _log;

        public FlightOperationsService(
            IFlights flights, IFlightInventories inventories, IEventPublisher events, ILogger<FlightOperationsService> log)
        {
            _flights = flights;
            _inventories = inventories;
            _events = events;
            _log = log;
        }

        /// <summary>Every flight departing <paramref name="station"/> on the given local
        /// operating day, with live seat counters attached.</summary>
        public async Task<IReadOnlyList<Flight>> ListDeparturesAsync(
            Guid companyId, string station, DateOnly date, CancellationToken ct = default)
        {
            var fromLocal = date.ToDateTime(TimeOnly.MinValue);
            var toLocal = date.ToDateTime(new TimeOnly(23, 59, 59));
            var flights = await _flights.FindDeparturesAsync(companyId, station, fromLocal, toLocal, status: null, ct);

            var ordered = flights.OrderBy(f => f.DepartureDateTimeLocal).ToList();
            foreach (var flight in ordered)
                flight.Counters = await LoadCountersAsync(flight.Id, ct);
            return ordered;
        }

        public Task<Flight?> GetFlightAsync(Guid companyId, Guid flightId, CancellationToken ct = default) =>
            GetOwnedAsync(companyId, flightId, ct);

        /// <summary>Advance a flight's operational status through the state machine
        /// and emit the operational event. Depart uses <c>flight.departed</c>;
        /// everything else <c>flight.status-changed</c>.</summary>
        public async Task<FlightOpResult> ChangeStatusAsync(
            Guid companyId, Guid flightId, string action, CancellationToken ct = default)
        {
            var flight = await GetOwnedAsync(companyId, flightId, ct);
            if (flight is null) return FlightOpResult.NotFound();

            var from = FlightStateMachine.Normalize(flight.Status);
            var to = FlightStateMachine.TryTransition(from, action);
            if (to is null)
                return FlightOpResult.Invalid(
                    $"Cannot '{action}' a flight in '{from}' status.",
                    FlightStateMachine.GetAvailableActions(from));

            var updated = flight with { Status = to, Updated = DateTime.UtcNow };
            var saved = await _flights.SaveAsync(updated, ct) ?? updated;

            var type = action == FlightStateMachine.Action.Depart ? "flight.departed" : "flight.status-changed";
            await _events.PublishAsync(type,
                new EventSubject("flights", saved.Id.ToString()),
                new { id = saved.Id, flightNumber = saved.FlightNumber, from, to, action },
                companyId, actor: "flight-operations", ct);

            saved.Counters = await LoadCountersAsync(saved.Id, ct);
            return FlightOpResult.Ok(saved);
        }

        private async Task<Flight?> GetOwnedAsync(Guid companyId, Guid flightId, CancellationToken ct)
        {
            var flight = await _flights.GetByIdAsync(flightId, ct);
            return flight is not null && flight.CompanyId == companyId ? flight : null;
        }

        private async Task<FlightCounters?> LoadCountersAsync(Guid flightId, CancellationToken ct)
        {
            try
            {
                var inv = await _inventories.GetByFlightAsync(flightId, ct);
                if (inv.Count == 0) return null;
                return new FlightCounters
                {
                    Capacity = inv.Sum(i => i.Capacity),
                    Sold = inv.Sum(i => i.Sold),
                    Available = inv.Sum(i => i.Available),
                };
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not load seat counters for flight {FlightId}.", flightId);
                return null;
            }
        }
    }
}
