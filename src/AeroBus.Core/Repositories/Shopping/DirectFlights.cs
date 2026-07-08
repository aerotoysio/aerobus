using AeroBus.Core.Common.Cache;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

using DistFlightSolution = AeroBus.Core.Model.Shopping.FlightSolution;
using DistFlightSegment = AeroBus.Core.Model.Shopping.FlightSegment;
using DistFlightEndpoint = AeroBus.Core.Model.Shopping.FlightEndpoint;
using CatFlightSolution = AeroBus.Core.Model.Catalogue.FlightSolution;

namespace AeroBus.Core.Repositories.Shopping
{
    /// <summary>
    /// Shop-facing flight search. Delegates to the connection-aware <see cref="IFlightSolutions"/>
    /// engine (direct + multi-stop, MCT-validated at every transfer) and projects the catalogue
    /// solution shape into the shopping shape consumed by the /shop pipeline.
    /// </summary>
    public interface IDirectFlightSolutions
    {
        Task<List<DistFlightSolution>> SearchAsync(
            string from,
            string to,
            DateTime? departureDate = null,
            int maxStops = 1,
            int maxSolutions = 20,
            CancellationToken ct = default);
    }

    public sealed class DirectFlightSolutions : IDirectFlightSolutions
    {
        private readonly IHotCache _cache;
        private readonly IFlightSolutions _engine;

        public DirectFlightSolutions(IHotCache cache, IFlightSolutions engine)
        {
            _cache = cache;
            _engine = engine;
        }

        public async Task<List<DistFlightSolution>> SearchAsync(
            string from,
            string to,
            DateTime? departureDate = null,
            int maxStops = 1,
            int maxSolutions = 20,
            CancellationToken ct = default)
        {
            var date = (departureDate ?? DateTime.UtcNow).Date;
            var cap = Math.Clamp(maxSolutions, 1, 50);

            var query = new FlightSolutionQuery(
                From: from,
                To: to,
                DepartureDate: DateOnly.FromDateTime(date),
                MaxStops: Math.Max(0, maxStops),
                MaxSolutions: cap,
                // Open the whole origin-local day so afternoon/evening departures aren't clipped by
                // the engine's first-leg window (which otherwise soft-caps at midnight + 12h).
                LatestDepartureLocal: new TimeSpan(23, 59, 59),
                EarliestDepartureLocal: TimeSpan.Zero);

            var solutions = await _engine.SearchKAsync(query, ct);
            if (solutions.Count == 0) return new List<DistFlightSolution>();

            // Index cached flights by id to enrich each leg with carrier / equipment / terminal detail.
            var flightsById = (_cache.TryGet<Flight>(CacheKeys.Flights, out var cachedFlights) ? cachedFlights : Array.Empty<Flight>())
                .Where(f => f.Id != Guid.Empty)
                .GroupBy(f => f.Id)
                .ToDictionary(g => g.Key, g => g.First());

            return solutions
                .Take(cap)
                .Select(s => ToDistribution(s, flightsById))
                .ToList();
        }

        private static DistFlightSolution ToDistribution(CatFlightSolution solution, IReadOnlyDictionary<Guid, Flight> flightsById)
        {
            var legs = solution.Legs ?? new List<FlightSolutionLeg>();
            var segments = legs.Select(leg =>
            {
                flightsById.TryGetValue(leg.FlightId, out var flight);

                return new DistFlightSegment
                {
                    Id = Guid.NewGuid(),
                    FlightRef = leg.FlightId.ToString(),
                    MarketingCarrier = flight?.MarketingCarrier,
                    MarketingFlightNumber = flight?.FlightNumber,
                    OperatingCarrier = flight?.OperatingCarrier,
                    EquipmentCode = flight?.EquipmentCode,
                    SegmentDurationMinutes = (int)(leg.ArrivalUtc - leg.DepartureUtc).TotalMinutes,
                    Departure = new DistFlightEndpoint
                    {
                        Airport = leg.From,
                        Terminal = flight?.DepartureTerminal,
                        ScheduledTimeLocal = leg.DepartureLocal
                    },
                    Arrival = new DistFlightEndpoint
                    {
                        Airport = leg.To,
                        Terminal = flight?.ArrivalTerminal,
                        ScheduledTimeLocal = leg.ArrivalLocal
                    }
                };
            }).ToList();

            return new DistFlightSolution
            {
                Id = Guid.NewGuid(),
                Flights = segments,
                ElapsedDurationMinutes = (int)solution.TotalElapsed.TotalMinutes
            };
        }
    }
}
