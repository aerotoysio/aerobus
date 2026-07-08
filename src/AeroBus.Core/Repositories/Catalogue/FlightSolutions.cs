using AeroBus.Core.Common.Cache;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IFlightSolutions
    {
        Task<IReadOnlyList<FlightSolution>> SearchKAsync(FlightSolutionQuery query, CancellationToken ct = default);
        Task<FlightSolution?> SearchAsync(FlightSolutionQuery query, CancellationToken ct = default);
        Task<FlightSolution?> SearchCsaAsync(FlightSolutionQuery query, CancellationToken ct = default);
    }

    public sealed class FlightSolutions : IFlightSolutions
    {
        private readonly IHotCache _cache;
        private readonly ITimeZoneResolver _tz;
        private readonly IMctResolver _mct;

        public FlightSolutions(IHotCache cache, ITimeZoneResolver tz, IMctResolver mct)
        {
            _cache = cache;
            _tz = tz;
            _mct = mct;
        }

        // The engines search the hot-cache flight snapshot. There is no boot
        // preloader in AeroBus: whoever drives a shopping flow seeds the
        // CacheKeys.Flights bucket first; a cold cache just yields no solutions
        // (TryGet, not Get, so it degrades instead of throwing).
        private Flight[] CachedFlights() =>
            _cache.TryGet<Flight>(CacheKeys.Flights, out var rows)
                ? rows.Where(f => !string.IsNullOrWhiteSpace(f.DepartureStation) && !string.IsNullOrWhiteSpace(f.ArrivalStation)).ToArray()
                : Array.Empty<Flight>();

        // ------------------ Top-K best-first search ------------------
        public Task<IReadOnlyList<FlightSolution>> SearchKAsync(FlightSolutionQuery q, CancellationToken ct = default)
        {
            var flights = CachedFlights();

            // Build departures index per station (sorted by UTC). Precompute at cache-load later if needed.
            var departuresByStation = flights
                .GroupBy(f => f.DepartureStation, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.DepartureDateTime).ToArray(),
                              StringComparer.OrdinalIgnoreCase);

            // Origin-local to UTC
            var originTz = _tz.Resolve(q.From);
            var startLocal = new DateTime(q.DepartureDate.Year, q.DepartureDate.Month, q.DepartureDate.Day,
                                          0, 0, 0, DateTimeKind.Unspecified);
            if (q.EarliestDepartureLocal is TimeSpan fromT)
                startLocal = startLocal.Date + fromT;
            var startUtc = ToUtc(startLocal, originTz);

            DateTime? latestOriginUtc = null;
            if (q.LatestDepartureLocal is TimeSpan toT)
            {
                var lastLocal = new DateTime(q.DepartureDate.Year, q.DepartureDate.Month, q.DepartureDate.Day,
                                             toT.Hours, toT.Minutes, toT.Seconds, DateTimeKind.Unspecified);
                latestOriginUtc = ToUtc(lastLocal, originTz);
            }

            var maxStops = Math.Max(0, q.MaxStops);
            var maxSolutions = Math.Clamp(q.MaxSolutions, 1, 200);
            var maxJourney = TimeSpan.FromHours(36); // guardrail

            var results = new List<FlightSolution>(maxSolutions);

            // Best-first over partial itineraries (priority: current arrival UTC)
            var pq = new PriorityQueue<State, DateTime>();
            pq.Enqueue(new State(q.From, startUtc, new List<Flight>(4), 0), startUtc);

            // prune dominated states: earliest arrival at (airport, stops)
            var bestSeen = new Dictionary<(string airport, int stops), DateTime>(1024);

            while (results.Count < maxSolutions && pq.TryDequeue(out var s, out _))
            {
                if (ct.IsCancellationRequested) break;

                // Destination reached with at least one leg → emit solution
                if (s.Legs.Count > 0 && s.Airport.Equals(q.To, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(BuildSolution(s.Legs));
                    continue;
                }

                if (s.StopsUsed > maxStops) continue;
                if (!departuresByStation.TryGetValue(s.Airport, out var outFlights)) continue;

                // Departure window at the current airport (UTC).
                DateTime minDepUtc;
                DateTime maxDepUtc;
                if (s.Legs.Count > 0)
                {
                    // Transfer: the onward leg must depart within [MCT.Min, MCT.Max] of arrival.
                    var mm = _mct.Resolve(s.Airport) ?? (Min: TimeSpan.FromMinutes(45), Max: TimeSpan.FromHours(6));
                    var tz = _tz.Resolve(s.Airport);
                    var arrLocal = ToLocal(s.ReadyUtc, tz);
                    minDepUtc = ToUtc(arrLocal + mm.Min, tz);
                    maxDepUtc = ToUtc(arrLocal + mm.Max, tz);
                }
                else
                {
                    // First leg: scan the whole requested origin-local day unless the caller bounded it.
                    // (Previously this soft-capped at midnight + 12h, silently dropping afternoon departures.)
                    minDepUtc = s.ReadyUtc;
                    maxDepUtc = latestOriginUtc ?? startUtc.AddHours(24);
                }

                // Binary search for first candidate >= minDepUtc
                int startIdx = LowerBound(outFlights, minDepUtc);

                // Iterate until we exceed maxDepUtc
                for (int i = startIdx; i < outFlights.Length; i++)
                {
                    var f = outFlights[i];
                    if (f.DepartureDateTime > maxDepUtc) break;

                    // First leg must not depart before startUtc
                    if (s.Legs.Count == 0 && f.DepartureDateTime < startUtc) continue;

                    // Avoid immediate backtrack A->B then B->A
                    if (s.Legs.Count > 0 && f.ArrivalStation.Equals(s.Airport, StringComparison.OrdinalIgnoreCase)) continue;

                    // Journey duration guard
                    var tripStart = s.Legs.Count == 0 ? f.DepartureDateTime : s.Legs[0].DepartureDateTime;
                    if (f.ArrivalDateTime - tripStart > maxJourney) continue;

                    // Dominance prune at intermediate hubs only: keep just the earliest arrival per
                    // (hub, stops) to bound branching. Do NOT prune arrivals at the destination, so the
                    // shopper sees every distinct itinerary that reaches it (multiple departure times and
                    // connections), ordered by arrival and capped by maxSolutions.
                    if (!f.ArrivalStation.Equals(q.To, StringComparison.OrdinalIgnoreCase))
                    {
                        (string airport, int stops) label = (f.ArrivalStation, s.StopsUsed + 1);
                        if (bestSeen.TryGetValue(label, out var seen) && seen <= f.ArrivalDateTime) continue;
                        bestSeen[label] = f.ArrivalDateTime;
                    }

                    // Extend itinerary
                    var legs = new List<Flight>(s.Legs.Count + 1);
                    legs.AddRange(s.Legs);
                    legs.Add(f);

                    pq.Enqueue(new State(f.ArrivalStation, f.ArrivalDateTime, legs, s.StopsUsed + 1), f.ArrivalDateTime);
                }
            }

            return Task.FromResult<IReadOnlyList<FlightSolution>>(results);
        }

        // ------------------ Connection Scan Algorithm (CSA) ------------------
        public Task<FlightSolution?> SearchCsaAsync(FlightSolutionQuery q, CancellationToken ct = default)
        {
            var flights = CachedFlights();

            var originTz = _tz.Resolve(q.From);
            var dayStartLocal = new DateTime(q.DepartureDate.Year, q.DepartureDate.Month, q.DepartureDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var dayEndLocal = dayStartLocal.AddDays(1).AddTicks(-1);
            if (q.EarliestDepartureLocal is { } fromT) dayStartLocal = dayStartLocal.Date + fromT;
            if (q.LatestDepartureLocal is { } toT) dayEndLocal = dayStartLocal.Date + toT;

            var T0 = ToUtc(dayStartLocal, originTz);
            var T1 = ToUtc(dayEndLocal, originTz).AddHours(24); // allow overnight connections

            // Slice flights in the window (cheap prefilter)
            var conns = flights
                .Where(f => f.DepartureDateTime >= T0 && f.DepartureDateTime <= T1)
                .OrderBy(f => f.DepartureDateTime)
                .ToArray();

            // Map airport codes to indices for dense arrays
            var airports = conns.SelectMany(f => new[] { f.DepartureStation, f.ArrivalStation })
                                .Append(q.From).Append(q.To)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Select((code, i) => (code, i))
                                .ToDictionary(t => t.code, t => t.i, StringComparer.OrdinalIgnoreCase);

            int N = airports.Count;
            int K = Math.Max(1, q.MaxStops + 1); // #rounds = #flights taken, up to MaxStops+1

            var INF = DateTime.MaxValue;
            var earliest = new DateTime[N, K + 1];
            var pred = new (int prevAirport, int prevR, Guid flightId)?[N, K + 1];

            for (int a = 0; a < N; a++)
                for (int r = 0; r <= K; r++)
                    earliest[a, r] = INF;

            earliest[airports[q.From], 0] = T0;

            // Round r relaxes flights to fill r+1
            for (int r = 0; r < K; r++)
            {
                foreach (var f in conns)
                {
                    if (ct.IsCancellationRequested) break;

                    int u = airports[f.DepartureStation];
                    int v = airports[f.ArrivalStation];

                    var readyAt = earliest[u, r];
                    if (readyAt == INF) continue;

                    // must be able to catch it
                    if (readyAt > f.DepartureDateTime) continue;

                    // MCT check at u (for transfers)
                    if (r > 0)
                    {
                        var mm = _mct.Resolve(f.DepartureStation) ?? (Min: TimeSpan.FromMinutes(45), Max: TimeSpan.FromHours(6));
                        var readyLocal = ToLocal(readyAt, _tz.Resolve(f.DepartureStation));
                        var depLocal = ToLocal(f.DepartureDateTime, _tz.Resolve(f.DepartureStation));
                        var layover = depLocal - readyLocal;
                        if (layover < mm.Min || layover > mm.Max) continue;
                    }

                    var arr = f.ArrivalDateTime;
                    if (arr < earliest[v, r + 1])
                    {
                        earliest[v, r + 1] = arr;
                        pred[v, r + 1] = (u, r, f.Id);
                    }
                }
            }

            // pick best arrival at destination
            if (!airports.TryGetValue(q.To, out var destIdx)) return Task.FromResult<FlightSolution?>(null);

            DateTime bestArr = INF; int bestR = -1;
            for (int r = 1; r <= K; r++)
            {
                if (earliest[destIdx, r] < bestArr)
                {
                    bestArr = earliest[destIdx, r];
                    bestR = r;
                }
            }
            if (bestR < 0) return Task.FromResult<FlightSolution?>(null);

            // reconstruct legs
            var legs = new List<FlightSolutionLeg>();
            int curA = destIdx, curR = bestR;

            while (curR > 0)
            {
                var p = pred[curA, curR]!.Value;
                var flightId = p.flightId;
                var fl = flights.First(x => x.Id == flightId);
                legs.Add(new FlightSolutionLeg(
                    fl.Id, fl.DepartureStation, fl.ArrivalStation,
                    fl.DepartureDateTime, fl.ArrivalDateTime,
                    ToLocal(fl.DepartureDateTime, _tz.Resolve(fl.DepartureStation)),
                    ToLocal(fl.ArrivalDateTime, _tz.Resolve(fl.ArrivalStation))
                ));
                curA = p.prevAirport;
                curR = p.prevR;
            }
            legs.Reverse();

            return Task.FromResult<FlightSolution?>(new FlightSolution(legs, bestArr - legs[0].DepartureUtc));
        }

        // ------------------ Simple direct / one-stop search ------------------
        public Task<FlightSolution?> SearchAsync(FlightSolutionQuery q, CancellationToken ct = default)
        {
            var originTz = _tz.Resolve(q.From);
            var dayStartLocal = new DateTime(q.DepartureDate.Year, q.DepartureDate.Month, q.DepartureDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var dayEndLocal = dayStartLocal.AddDays(1).AddTicks(-1);

            if (q.EarliestDepartureLocal is { } fromTime)
                dayStartLocal = new DateTime(q.DepartureDate.Year, q.DepartureDate.Month, q.DepartureDate.Day, fromTime.Hours, fromTime.Minutes, fromTime.Seconds, DateTimeKind.Unspecified);
            if (q.LatestDepartureLocal is { } toTime)
                dayEndLocal = new DateTime(q.DepartureDate.Year, q.DepartureDate.Month, q.DepartureDate.Day, toTime.Hours, toTime.Minutes, toTime.Seconds, DateTimeKind.Unspecified);

            var winStartUtc = ToUtc(dayStartLocal, originTz);
            var winEndUtc = ToUtc(dayEndLocal, originTz).AddHours(12);

            var flights = CachedFlights();

            // pre-filter: departures from origin in window
            var dep = flights.Where(f =>
                    f.DepartureStation.Equals(q.From, StringComparison.OrdinalIgnoreCase) &&
                    f.DepartureDateTime >= winStartUtc && f.DepartureDateTime <= winEndUtc)
                .OrderBy(f => f.DepartureDateTime)
                .ToArray();

            FlightSolution? best = null;

            // 1) direct
            foreach (var f in dep.Where(f => f.ArrivalStation.Equals(q.To, StringComparison.OrdinalIgnoreCase)))
            {
                var total = f.ArrivalDateTime - f.DepartureDateTime;
                best = Better(best, new[] { Leg(f, _tz) }, total);
            }

            // 2) one-stop
            if (q.MaxStops >= 1)
            {
                foreach (var f1 in dep)
                {
                    var mm = _mct.Resolve(f1.ArrivalStation) ?? (Min: TimeSpan.FromMinutes(45), Max: TimeSpan.FromHours(6));
                    var f1ArrLocal = ToLocal(f1.ArrivalDateTime, _tz.Resolve(f1.ArrivalStation));
                    var minDepLocal = f1ArrLocal + mm.Min;
                    var maxDepLocal = f1ArrLocal + mm.Max;
                    var minDepUtc = ToUtc(minDepLocal, _tz.Resolve(f1.ArrivalStation));
                    var maxDepUtc = ToUtc(maxDepLocal, _tz.Resolve(f1.ArrivalStation));

                    var leg2 = flights.Where(f =>
                                f.DepartureStation.Equals(f1.ArrivalStation, StringComparison.OrdinalIgnoreCase) &&
                                f.DepartureDateTime >= minDepUtc && f.DepartureDateTime <= maxDepUtc &&
                                f.ArrivalStation.Equals(q.To, StringComparison.OrdinalIgnoreCase))
                                      .OrderBy(f => f.DepartureDateTime);

                    foreach (var f2 in leg2)
                    {
                        if (f2.DepartureDateTime < f1.ArrivalDateTime) continue;
                        var total = f2.ArrivalDateTime - f1.DepartureDateTime;
                        best = Better(best, new[] { Leg(f1, _tz), Leg(f2, _tz) }, total);
                    }
                }
            }

            return Task.FromResult(best);
        }

        // ------------------ Helpers ------------------

        private static int LowerBound(Flight[] arr, DateTime valueUtc)
        {
            int lo = 0, hi = arr.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (arr[mid].DepartureDateTime >= valueUtc) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }

        private FlightSolution BuildSolution(List<Flight> legs)
        {
            var solLegs = legs.Select(fl => new FlightSolutionLeg(
                fl.Id, fl.DepartureStation, fl.ArrivalStation,
                fl.DepartureDateTime, fl.ArrivalDateTime,
                ToLocal(fl.DepartureDateTime, _tz.Resolve(fl.DepartureStation)),
                ToLocal(fl.ArrivalDateTime, _tz.Resolve(fl.ArrivalStation))
            )).ToList();

            var total = legs[^1].ArrivalDateTime - legs[0].DepartureDateTime;
            return new FlightSolution(solLegs, total);
        }

        private static FlightSolution? Better(FlightSolution? current, IEnumerable<FlightSolutionLeg> legs, TimeSpan total)
        {
            var sol = new FlightSolution(legs.ToList(), total);
            return current is null || total < current.TotalElapsed ? sol : current;
        }

        private static FlightSolutionLeg Leg(Flight f, ITimeZoneResolver tzr)
        {
            var depLocal = ToLocal(f.DepartureDateTime, tzr.Resolve(f.DepartureStation));
            var arrLocal = ToLocal(f.ArrivalDateTime, tzr.Resolve(f.ArrivalStation));
            return new FlightSolutionLeg(
                f.Id, f.DepartureStation, f.ArrivalStation,
                f.DepartureDateTime, f.ArrivalDateTime,
                depLocal, arrLocal
            );
        }

        private static DateTime ToUtc(DateTime local, TimeZoneInfo? tz)
        {
            if (tz is null) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            if (tz.IsInvalidTime(local)) local = local.AddHours(1);
            else if (tz.IsAmbiguousTime(local))
            {
                var offsets = tz.GetAmbiguousTimeOffsets(local);
                var standard = offsets.MaxBy(o => o);
                return new DateTimeOffset(local, standard).UtcDateTime;
            }
            return TimeZoneInfo.ConvertTimeToUtc(local, tz);
        }

        private static DateTime ToLocal(DateTime utc, TimeZoneInfo? tz)
        {
            if (utc.Kind != DateTimeKind.Utc) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return tz is null ? utc : TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }

        // Keep State in class scope so it's always visible where used
        private sealed record State(string Airport, DateTime ReadyUtc, List<Flight> Legs, int StopsUsed);
    }
}
