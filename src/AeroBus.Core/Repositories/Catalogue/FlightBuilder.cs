using AeroBus.Core.Data;
using AeroBus.Core.Common.Cache;
using AeroBus.Core.Events;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Model.Stock;
using AeroBus.Core.Repositories.Stock;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IFlightBuilder
    {
        Task<IReadOnlyList<Flight>> PreviewAsync(Guid scheduleId, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> BuildAsync(Guid scheduleId, CancellationToken ct = default);
        Task<IReadOnlyList<Flight>> RefreshAsync(Guid scheduleId, CancellationToken ct = default);
        Task<bool> RefreshAllAsync(Guid companyId, CancellationToken ct = default);
    }

    /// <summary>
    /// Expands a schedule into dated Flight instances and — new in AeroBus —
    /// initialises the per-flight seat inventory: one FlightInventory document
    /// per layout compartment (Bucket = compartment code, Capacity = seats in
    /// the compartment), or a single "ALL" bucket from the schedule/flight
    /// capacity when the schedule has no usable layout. Flight.Counters stays
    /// on the flight document as a display denormalisation of those buckets.
    /// </summary>
    public sealed class FlightBuilder : IFlightBuilder
    {
        private readonly ISchedules _schedules;
        private readonly IFlights _flights;
        private readonly ILayouts _layouts;
        private readonly IFlightInventories _inventories;
        private readonly ITimeZoneResolver _tz;
        private readonly IEventPublisher _events;

        // (The ooms constructor also took IAirportResolver + IHotCache; both were
        // dead in the extraction path and are dropped here.)
        public FlightBuilder(ISchedules schedules, IFlights flights, ILayouts layouts, IFlightInventories inventories, ITimeZoneResolver tz, IEventPublisher events)
        {
            _schedules = schedules;
            _flights = flights;
            _layouts = layouts;
            _inventories = inventories;
            _tz = tz;
            _events = events;
        }

        public async Task<bool> RefreshAllAsync(Guid companyId, CancellationToken ct = default)
        {
            var companySchedules = await _schedules.GetAllByCompanyAsync(companyId, "Approved", ct);
            foreach (var schedule in companySchedules)
                await BuildAsync(schedule.Id, ct);
            return true;
        }

        public async Task<IReadOnlyList<Flight>> PreviewAsync(Guid scheduleId, CancellationToken ct = default)
        {
            var schedule = await _schedules.GetByIdAsync(scheduleId, ct);
            if (schedule is null) return Array.Empty<Flight>();
            var layout = await GetLayoutAsync(schedule, ct);
            return ExpandSchedule(schedule, layout);
        }

        public async Task<IReadOnlyList<Flight>> BuildAsync(Guid scheduleId, CancellationToken ct = default)
        {
            var schedule = await _schedules.GetByIdAsync(scheduleId, ct);
            if (schedule is null) return Array.Empty<Flight>();

            var layout = await GetLayoutAsync(schedule, ct);

            foreach (var candidate in ExpandSchedule(schedule, layout))
            {
                await _flights.SaveAsync(candidate, ct);
                await CreateInventoryAsync(candidate, layout, ct);
                await PublishFlightBuiltAsync(candidate, ct);
            }

            schedule.Status = "Built";
            await _schedules.SaveAsync(schedule, ct);
            await PublishScheduleChangedAsync(schedule, ct);

            return await _flights.GetByScheduleIdAsync(scheduleId, ct);
        }

        public async Task<IReadOnlyList<Flight>> RefreshAsync(Guid scheduleId, CancellationToken ct = default)
        {
            var newSchedule = await _schedules.GetByIdAsync(scheduleId, ct)
                ?? throw new InvalidOperationException($"Schedule {scheduleId} not found.");

            if (newSchedule.GroupingId is null)
                throw new InvalidOperationException("Schedule must have a GroupingId to refresh.");

            var previousSchedule = await _schedules.GetPreviousByGroupingIdAsync(newSchedule.GroupingId.Value, newSchedule.Id, ct)
                ?? throw new InvalidOperationException($"Previous schedule not found for grouping {newSchedule.GroupingId}.");

            var scheduleChanges = DetermineChangeType(newSchedule, previousSchedule);

            bool hasRouteOrNumberChange =
                scheduleChanges.Contains(ScheduleChangeType.DepartureStationChange) ||
                scheduleChanges.Contains(ScheduleChangeType.ArrivalStationChange) ||
                scheduleChanges.Contains(ScheduleChangeType.FlightNumberChange);

            bool isCancelled =
                scheduleChanges.Contains(ScheduleChangeType.Cancellation) ||
                string.Equals(newSchedule.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            var previousFlights = await _flights.GetByScheduleIdAsync(previousSchedule.Id, ct);
            var newLayout = await GetLayoutAsync(newSchedule, ct);

            foreach (var previousFlight in previousFlights)
            {
                ct.ThrowIfCancellationRequested();

                var flightDateLocal = DateOnly.FromDateTime(previousFlight.DepartureDateTimeLocal);

                bool inNewRange =
                    flightDateLocal >= DateOnly.FromDateTime(newSchedule.StartDateLocal) &&
                    flightDateLocal <= DateOnly.FromDateTime(newSchedule.EndDateLocal);

                bool operatedByPrevious = RunsOn(previousSchedule, flightDateLocal);
                bool operatedByNew = inNewRange && RunsOn(newSchedule, flightDateLocal);

                if (isCancelled)
                {
                    await CancelFlightAsync(previousFlight, newSchedule.Id, ct);
                    continue;
                }

                if (!operatedByNew && operatedByPrevious)
                {
                    await CancelFlightAsync(previousFlight, newSchedule.Id, ct);
                    continue;
                }

                if (!operatedByPrevious) continue;

                if (hasRouteOrNumberChange)
                {
                    await CancelFlightAsync(previousFlight, newSchedule.Id, ct);
                    var replacement = BuildFlightInstance(newSchedule, flightDateLocal, newLayout);
                    await _flights.SaveAsync(replacement, ct);
                    await CreateInventoryAsync(replacement, newLayout, ct);
                    await PublishFlightBuiltAsync(replacement, ct);
                    continue;
                }

                var newTemplate = BuildFlightInstance(newSchedule, flightDateLocal, newLayout);
                var updatedFlight = previousFlight with
                {
                    ScheduleId = newSchedule.Id,
                    DepartureDateTimeLocal = newTemplate.DepartureDateTimeLocal,
                    ArrivalDateTimeLocal = newTemplate.ArrivalDateTimeLocal,
                    DepartureDateTime = newTemplate.DepartureDateTime,
                    ArrivalDateTime = newTemplate.ArrivalDateTime,
                    EquipmentCode = newTemplate.EquipmentCode,
                    LayoutId = newTemplate.LayoutId,
                    DepartureTerminal = newTemplate.DepartureTerminal,
                    ArrivalTerminal = newTemplate.ArrivalTerminal,
                    Tags = newTemplate.Tags,
                    Updated = DateTime.UtcNow
                };
                // In-place retime keeps the flight id, so its inventory documents
                // (and any sales recorded against them) carry over untouched.
                await _flights.SaveAsync(updatedFlight, ct);
            }

            newSchedule.Status = isCancelled ? "Cancelled" : "Built";
            await _schedules.SaveAsync(newSchedule, ct);

            previousSchedule.Status = "Replaced";
            await _schedules.SaveAsync(previousSchedule, ct);
            await PublishScheduleChangedAsync(newSchedule, ct);

            return await _flights.GetByScheduleIdAsync(newSchedule.Id, ct);
        }

        public enum ScheduleChangeType
        {
            DepartureTimeChange, ArrivalTimeChange, FlightNumberChange, ArrivalStationChange, DepartureStationChange,
            ArrivalTerminalChange, DepartureTerminalChange, MondayOperation, TuesdayOperation, WednesdayOperation,
            ThursdayOperation, FridayOperation, SaturdayOperation, SundayOperation, StartDateChange, EndDateChange,
            Cancellation, NoChange
        }

        private static List<ScheduleChangeType> DetermineChangeType(Schedule newSchedule, Schedule previousSchedule)
        {
            var c = new List<ScheduleChangeType>();
            if (newSchedule.DepartureTimeLocal != previousSchedule.DepartureTimeLocal) c.Add(ScheduleChangeType.DepartureTimeChange);
            if (newSchedule.ArrivalTimeLocal != previousSchedule.ArrivalTimeLocal) c.Add(ScheduleChangeType.ArrivalTimeChange);
            if (newSchedule.FlightNumber != previousSchedule.FlightNumber) c.Add(ScheduleChangeType.FlightNumberChange);
            if (newSchedule.ArrivalStation != previousSchedule.ArrivalStation) c.Add(ScheduleChangeType.ArrivalStationChange);
            if (newSchedule.DepartureStation != previousSchedule.DepartureStation) c.Add(ScheduleChangeType.DepartureStationChange);
            if (newSchedule.ArrivalTerminal != previousSchedule.ArrivalTerminal) c.Add(ScheduleChangeType.ArrivalTerminalChange);
            if (newSchedule.DepartureTerminal != previousSchedule.DepartureTerminal) c.Add(ScheduleChangeType.DepartureTerminalChange);
            if (newSchedule.Monday != previousSchedule.Monday) c.Add(ScheduleChangeType.MondayOperation);
            if (newSchedule.Tuesday != previousSchedule.Tuesday) c.Add(ScheduleChangeType.TuesdayOperation);
            if (newSchedule.Wednesday != previousSchedule.Wednesday) c.Add(ScheduleChangeType.WednesdayOperation);
            if (newSchedule.Thursday != previousSchedule.Thursday) c.Add(ScheduleChangeType.ThursdayOperation);
            if (newSchedule.Friday != previousSchedule.Friday) c.Add(ScheduleChangeType.FridayOperation);
            if (newSchedule.Saturday != previousSchedule.Saturday) c.Add(ScheduleChangeType.SaturdayOperation);
            if (newSchedule.Sunday != previousSchedule.Sunday) c.Add(ScheduleChangeType.SundayOperation);
            if (newSchedule.StartDateLocal != previousSchedule.StartDateLocal) c.Add(ScheduleChangeType.StartDateChange);
            if (newSchedule.EndDateLocal != previousSchedule.EndDateLocal) c.Add(ScheduleChangeType.EndDateChange);
            if (!string.Equals(newSchedule.Status, previousSchedule.Status, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(newSchedule.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                c.Add(ScheduleChangeType.Cancellation);
            if (!c.Any()) c.Add(ScheduleChangeType.NoChange);
            return c;
        }

        // ---------- core expansion ----------

        private IReadOnlyList<Flight> ExpandSchedule(Schedule schedule, Layout? layout)
        {
            if (schedule.StartDateLocal > schedule.EndDateLocal) return Array.Empty<Flight>();

            var flightList = new List<Flight>();
            for (var scheduleDay = schedule.StartDateLocal; scheduleDay <= schedule.EndDateLocal; scheduleDay = scheduleDay.AddDays(1))
            {
                if (!RunsOn(schedule, DateOnly.FromDateTime(scheduleDay))) continue;
                flightList.Add(BuildFlightInstance(schedule, DateOnly.FromDateTime(scheduleDay), layout));
            }
            return flightList;
        }

        private Flight BuildFlightInstance(Schedule schedule, DateOnly scheduleDay, Layout? layout)
        {
            var depTz = _tz.Resolve(schedule.DepartureStation);
            var arrTz = _tz.Resolve(schedule.ArrivalStation);

            var depLocal = ComposeLocal(scheduleDay, schedule.DepartureTimeLocal);
            var depUtc = ToUtc(depLocal, depTz);

            var arrLocal = ComposeLocal(scheduleDay.AddDays(schedule.ArrivalOffsetDays), schedule.ArrivalTimeLocal);
            var arrUtc = ToUtc(arrLocal, arrTz);

            var blockMinutes = (int)(arrUtc - depUtc).TotalMinutes;

            // Counters are the display total of the per-bucket inventory: the
            // compartment sum when a layout drives the buckets, otherwise the
            // schedule capacity backing the single "ALL" bucket.
            var totalCapacity = HasCompartments(layout)
                ? BucketsFor(schedule.Capacity, layout).Sum(b => b.Capacity)
                : schedule.Capacity;

            return new Flight
            {
                Id = Guid.NewGuid(),
                ConcurrencyId = Guid.NewGuid(),
                ScheduleId = schedule.Id,
                CompanyId = schedule.CompanyId,
                LayoutId = schedule.LayoutId,
                DepartureStation = schedule.DepartureStation,
                ArrivalStation = schedule.ArrivalStation,
                DepartureDateTimeLocal = depLocal,
                ArrivalDateTimeLocal = arrLocal,
                DepartureDateTime = depUtc,
                ArrivalDateTime = arrUtc,
                MarketingCarrier = schedule.MarketingCarrier ?? schedule.CarrierCode,
                OperatingCarrier = schedule.OperatingCarrier ?? schedule.MarketingCarrier ?? schedule.CarrierCode,
                FlightNumber = schedule.FlightNumber,
                EquipmentCode = schedule.EquipmentCode,
                DepartureTerminal = schedule.DepartureTerminal,
                ArrivalTerminal = schedule.ArrivalTerminal,
                BlockMinutes = blockMinutes,
                DistanceNm = 0,
                CostAmount = schedule.CostAmount,
                CostCurrency = schedule.CostCurrency,
                ExpectedLoadFactor = schedule.ExpectedLoadFactor,
                Capacity = schedule.Capacity,
                Data = null,
                Tags = schedule.Tags,
                Status = "Scheduled",
                Created = DateTime.UtcNow,
                Updated = null,
                Counters = new FlightCounters { Capacity = totalCapacity, Sold = 0, Available = totalCapacity }
            };
        }

        // ---------- inventory ----------

        private static bool HasCompartments(Layout? layout) =>
            layout?.Compartments is { Count: > 0 };

        /// <summary>
        /// One bucket per layout compartment (Bucket = compartment code, Capacity =
        /// seats in the compartment, falling back to its StockCapacity when the seat
        /// map isn't drawn). Without a usable layout: a single "ALL" bucket sized by
        /// the schedule/flight total capacity (0 if none is set anywhere).
        /// </summary>
        private static IReadOnlyList<(string Bucket, int Capacity)> BucketsFor(int? totalCapacity, Layout? layout)
        {
            if (HasCompartments(layout))
            {
                return layout!.Compartments!
                    .Select(c => (
                        Bucket: !string.IsNullOrWhiteSpace(c.Code) ? c.Code! :
                                !string.IsNullOrWhiteSpace(c.Name) ? c.Name! : c.Id.ToString("N"),
                        Capacity: c.Seats is { Count: > 0 } seats ? seats.Count : c.StockCapacity ?? 0))
                    .ToList();
            }

            return new[] { (Bucket: "ALL", Capacity: totalCapacity ?? 0) };
        }

        private Task<Layout?> GetLayoutAsync(Schedule schedule, CancellationToken ct) =>
            schedule.LayoutId == Guid.Empty
                ? Task.FromResult<Layout?>(null)
                : _layouts.GetByIdAsync(schedule.LayoutId, ct);

        private async Task CreateInventoryAsync(Flight flight, Layout? layout, CancellationToken ct)
        {
            foreach (var (bucket, capacity) in BucketsFor(flight.Capacity, layout))
            {
                await _inventories.SaveAsync(new FlightInventory
                {
                    Id = Guid.NewGuid(),
                    CompanyId = flight.CompanyId,
                    FlightId = flight.Id,
                    Bucket = bucket,
                    Capacity = capacity,
                    Sold = 0,
                    Available = capacity,
                    Created = DateTime.UtcNow,
                    Updated = null,
                    Status = "Active"
                }, ct);
            }
        }

        /// <summary>
        /// A schedule change removed/cancelled this flight: mark the flight
        /// Cancelled and mark its inventory documents Cancelled to match —
        /// mirrored (not deleted) because refresh keeps the flight document too.
        /// </summary>
        private async Task CancelFlightAsync(Flight flight, Guid newScheduleId, CancellationToken ct)
        {
            var cancelled = flight with { ScheduleId = newScheduleId, Status = "Cancelled", Updated = DateTime.UtcNow };
            await _flights.SaveAsync(cancelled, ct);
            foreach (var inv in await _inventories.GetByFlightAsync(flight.Id, ct))
                await _inventories.SaveAsync(inv with { Status = "Cancelled", Updated = DateTime.UtcNow }, ct);
            await PublishFlightCancelledAsync(cancelled, ct);
        }

        // ── event emit helpers ─────────────────────────────────────────────────
        // Best-effort: IEventPublisher.PublishAsync never throws, so a publish
        // failure can't abort a build/refresh.

        private Task PublishFlightBuiltAsync(Flight flight, CancellationToken ct) =>
            _events.PublishAsync("flight.built",
                new EventSubject(DfCollections.Catalogue.Flights, flight.Id.ToString()),
                new
                {
                    id = flight.Id,
                    scheduleId = flight.ScheduleId,
                    flightNumber = flight.FlightNumber,
                    origin = flight.DepartureStation,
                    destination = flight.ArrivalStation,
                    departureUtc = flight.DepartureDateTime,
                    arrivalUtc = flight.ArrivalDateTime,
                },
                flight.CompanyId, actor: "flight-builder", ct);

        private Task PublishFlightCancelledAsync(Flight flight, CancellationToken ct) =>
            _events.PublishAsync("flight.cancelled",
                new EventSubject(DfCollections.Catalogue.Flights, flight.Id.ToString()),
                new
                {
                    id = flight.Id,
                    scheduleId = flight.ScheduleId,
                    flightNumber = flight.FlightNumber,
                    origin = flight.DepartureStation,
                    destination = flight.ArrivalStation,
                    departureUtc = flight.DepartureDateTime,
                },
                flight.CompanyId, actor: "flight-builder", ct);

        private Task PublishScheduleChangedAsync(Schedule schedule, CancellationToken ct) =>
            _events.PublishAsync("schedule.changed",
                new EventSubject(DfCollections.Catalogue.Schedules, schedule.Id.ToString()),
                new
                {
                    id = schedule.Id,
                    status = schedule.Status,
                    flightNumber = schedule.FlightNumber,
                    groupingId = schedule.GroupingId,
                },
                schedule.CompanyId, actor: "flight-builder", ct);

        // ---------- helpers ----------

        private static bool RunsOn(Schedule s, DateOnly date) =>
            date.DayOfWeek switch
            {
                DayOfWeek.Monday => s.Monday,
                DayOfWeek.Tuesday => s.Tuesday,
                DayOfWeek.Wednesday => s.Wednesday,
                DayOfWeek.Thursday => s.Thursday,
                DayOfWeek.Friday => s.Friday,
                DayOfWeek.Saturday => s.Saturday,
                DayOfWeek.Sunday => s.Sunday,
                _ => false
            };

        private static DateTime ComposeLocal(DateOnly day, TimeSpan time) =>
            new(day.Year, day.Month, day.Day, time.Hours, time.Minutes, time.Seconds, DateTimeKind.Unspecified);

        private static DateTime ToUtc(DateTime local, TimeZoneInfo? tz)
        {
            if (tz is null) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            if (tz.IsInvalidTime(local)) local = local.AddHours(1);
            else if (tz.IsAmbiguousTime(local))
            {
                var standard = tz.GetAmbiguousTimeOffsets(local).MaxBy(o => o);
                return new DateTimeOffset(local, standard).UtcDateTime;
            }
            return TimeZoneInfo.ConvertTimeToUtc(local, tz);
        }
    }
}
