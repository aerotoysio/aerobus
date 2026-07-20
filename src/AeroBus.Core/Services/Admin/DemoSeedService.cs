using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Catalogue;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Admin
{
    public sealed record DemoSeedSection(string Key, string Label, int Planned, int Existing);
    public sealed record DemoSeedManifest(bool Seeded, IReadOnlyList<DemoSeedSection> Sections);
    public sealed record DemoSeedResult(string Key, int Created, int Total);

    /// <summary>
    /// The narrated demo seed: turns a freshly-onboarded org into a working demo
    /// airline from one flat definition (<c>demo-seed.json</c>, embedded) — hub
    /// airports, aircraft types, demo markets flown under the org's OWN designator
    /// for a rolling two-week window, then real flights via the flight builder.
    ///
    /// Runs section by section so the onboarding UI can show live progress
    /// ("Loading airports… ✓ 8"). Every section is idempotent (natural-key skip) and
    /// every document is stamped with the org's CompanyId and tagged <c>demo</c> so
    /// the set is identifiable (and removable) later. Writes go through the
    /// tenant-routed <see cref="IDocumentStore"/> — callers must be authenticated
    /// members of the org being seeded.
    /// </summary>
    public sealed class DemoSeedService
    {
        public const string SeededConfigKey = "demo.seeded";
        public const string DemoTag = "demo";

        public static class Sections
        {
            public const string Airports = "airports";
            public const string Equipment = "equipment";
            public const string Markets = "markets";
            public const string Flights = "flights";
        }

        private readonly IDocumentStore _store;
        private readonly ICompanies _companies;
        private readonly ICompanyConfigs _configs;
        private readonly IFlightBuilder _flightBuilder;
        private readonly ILogger<DemoSeedService> _log;

        public DemoSeedService(
            IDocumentStore store, ICompanies companies, ICompanyConfigs configs,
            IFlightBuilder flightBuilder, ILogger<DemoSeedService> log)
        {
            _store = store;
            _companies = companies;
            _configs = configs;
            _flightBuilder = flightBuilder;
            _log = log;
        }

        public async Task<DemoSeedManifest> GetManifestAsync(Guid companyId, CancellationToken ct = default)
        {
            var seed = Definition.Value;
            var schedulesPlanned = seed.Markets.Sum(m => m.Flights.Count);

            var seeded = await _configs.GetByIdAsync(companyId, SeededConfigKey, ct) is { Value: "true" };
            var sections = new List<DemoSeedSection>
            {
                new(Sections.Airports, "Airports", seed.Airports.Count,
                    await _store.CountAsync(DfCollections.Catalogue.Airports, ByCompany(companyId), ct)),
                new(Sections.Equipment, "Equipment types", seed.Equipment.Count,
                    await _store.CountAsync(DfCollections.Catalogue.Equipment, ByCompany(companyId), ct)),
                new(Sections.Markets, "Demo markets & schedules", schedulesPlanned,
                    await _store.CountAsync(DfCollections.Catalogue.Schedules, ByDemo(companyId), ct)),
                new(Sections.Flights, "Flights", schedulesPlanned * seed.Days,
                    await _store.CountAsync(DfCollections.Catalogue.Flights, ByDemo(companyId), ct)),
            };
            return new DemoSeedManifest(seeded, sections);
        }

        /// <summary>Run one seed section. Unknown keys throw <see cref="ArgumentException"/>.</summary>
        public async Task<DemoSeedResult> SeedSectionAsync(Guid companyId, string section, CancellationToken ct = default)
        {
            var company = await _companies.GetByIdAsync(companyId, ct)
                ?? throw new InvalidOperationException("Company not found — has this organisation been provisioned?");

            var result = section switch
            {
                Sections.Airports => await SeedAirportsAsync(companyId, ct),
                Sections.Equipment => await SeedEquipmentAsync(companyId, ct),
                Sections.Markets => await SeedMarketsAsync(company, ct),
                Sections.Flights => await BuildFlightsAsync(companyId, ct),
                _ => throw new ArgumentException($"Unknown seed section '{section}'.", nameof(section)),
            };

            _log.LogInformation("Demo seed section {Section} for company {CompanyId}: created {Created}/{Total}.",
                section, companyId, result.Created, result.Total);
            return result;
        }

        // ─── sections ───────────────────────────────────────────────────────────

        private async Task<DemoSeedResult> SeedAirportsAsync(Guid companyId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var created = 0;
            foreach (var a in Definition.Value.Airports)
            {
                var existing = await _store.CountAsync(DfCollections.Catalogue.Airports,
                    new Dictionary<string, object?> { [Df.CompanyId] = companyId, [Df.Field(nameof(Airport.Code))] = a.Code }, ct);
                if (existing > 0) continue;

                var airport = new Airport
                {
                    Id = Guid.NewGuid(), Code = a.Code, Name = a.Name, City = a.City,
                    TimeZoneId = a.Tz, Status = "Active", Tags = DemoTag,
                    Created = now, CompanyId = companyId,
                };
                await _store.UpsertAsync(DfCollections.Catalogue.Airports, airport, airport.Id, ct);
                created++;
            }
            return new DemoSeedResult(Sections.Airports, created, Definition.Value.Airports.Count);
        }

        private async Task<DemoSeedResult> SeedEquipmentAsync(Guid companyId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var created = 0;
            foreach (var e in Definition.Value.Equipment)
            {
                var existing = await _store.CountAsync(DfCollections.Catalogue.Equipment,
                    new Dictionary<string, object?> { [Df.CompanyId] = companyId, [Df.Field(nameof(Model.Catalogue.Equipment.EquipmentCode))] = e.Code }, ct);
                if (existing > 0) continue;

                var equipment = new Equipment
                {
                    Id = Guid.NewGuid(), EquipmentCode = e.Code, Name = e.Name,
                    RangeNm = e.RangeNm, Status = "Active", Tags = DemoTag,
                    Created = now, CompanyId = companyId,
                };
                await _store.UpsertAsync(DfCollections.Catalogue.Equipment, equipment, equipment.Id, ct);
                created++;
            }
            return new DemoSeedResult(Sections.Equipment, created, Definition.Value.Equipment.Count);
        }

        private async Task<DemoSeedResult> SeedMarketsAsync(Company company, CancellationToken ct)
        {
            var seed = Definition.Value;
            var companyId = company.Id;
            var carrier = string.IsNullOrWhiteSpace(company.Designator) ? "XX" : company.Designator!;
            var capacityByEquipment = seed.Equipment.ToDictionary(e => e.Code, e => e.Capacity);
            var start = DateTime.UtcNow.Date;
            var end = start.AddDays(seed.Days - 1);
            var now = DateTime.UtcNow;
            var created = 0;
            var total = 0;

            foreach (var market in seed.Markets)
            foreach (var f in market.Flights)
            {
                total++;
                var (from, to) = f.Return ? (market.Destination, market.Origin) : (market.Origin, market.Destination);

                var existing = await _store.CountAsync(DfCollections.Catalogue.Schedules, new Dictionary<string, object?>
                {
                    [Df.CompanyId] = companyId, [Df.Field(nameof(Schedule.FlightNumber))] = f.Number, [Df.Field(nameof(Schedule.DepartureStation))] = from,
                }, ct);
                if (existing > 0) continue;

                var schedule = new Schedule
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    CarrierCode = carrier,
                    FlightNumber = f.Number,
                    DepartureStation = from,
                    ArrivalStation = to,
                    DepartureTimeLocal = TimeSpan.Parse(f.Dep),
                    ArrivalTimeLocal = TimeSpan.Parse(f.Arr),
                    ArrivalOffsetDays = f.ArrOffsetDays,
                    StartDateLocal = start,
                    EndDateLocal = end,
                    Monday = true, Tuesday = true, Wednesday = true, Thursday = true,
                    Friday = true, Saturday = true, Sunday = true,
                    EquipmentCode = f.Equipment,
                    Capacity = capacityByEquipment.GetValueOrDefault(f.Equipment, 180),
                    MarketingCarrier = carrier,
                    OperatingCarrier = carrier,
                    Status = "Active",
                    Tags = DemoTag,
                    Created = now,
                    ConcurrencyId = Guid.NewGuid(),
                };
                await _store.UpsertAsync(DfCollections.Catalogue.Schedules, schedule, schedule.Id, ct);
                created++;
            }
            return new DemoSeedResult(Sections.Markets, created, total);
        }

        /// <summary>Build dated flights (+ seat inventory) for every unbuilt demo
        /// schedule, then mark the org as demo-seeded. Built schedules are skipped
        /// (the builder stamps them "Built"), so re-runs are no-ops.</summary>
        private async Task<DemoSeedResult> BuildFlightsAsync(Guid companyId, CancellationToken ct)
        {
            var schedules = await _store.QueryAsync<Schedule>(DfCollections.Catalogue.Schedules,
                new Dictionary<string, object?> { [Df.CompanyId] = companyId, [Df.Field(nameof(Schedule.Tags))] = DemoTag }, ct: ct);

            var built = 0;
            foreach (var schedule in schedules.Where(s => !string.Equals(s.Status, "Built", StringComparison.OrdinalIgnoreCase)))
            {
                var flights = await _flightBuilder.BuildAsync(schedule.Id, ct);
                built += flights.Count;
            }

            await _configs.SaveAsync(new CompanyConfig
            {
                CompanyId = companyId,
                Key = SeededConfigKey,
                Value = "true",
                Description = "Demo airline seed completed (airports, equipment, markets, flights).",
            }, ct);

            var totalFlights = await _store.CountAsync(DfCollections.Catalogue.Flights, ByDemo(companyId), ct);
            return new DemoSeedResult(Sections.Flights, built, totalFlights);
        }

        // ─── the flat seed definition ───────────────────────────────────────────

        private static Dictionary<string, object?> ByCompany(Guid companyId) =>
            new() { [Df.CompanyId] = companyId };

        private static Dictionary<string, object?> ByDemo(Guid companyId) =>
            new() { [Df.CompanyId] = companyId, [Df.Field(nameof(Schedule.Tags))] = DemoTag };

        private static readonly Lazy<SeedFile> Definition = new(Load);

        private static SeedFile Load()
        {
            var assembly = typeof(DemoSeedService).Assembly;
            const string name = "AeroBus.Core.Services.Admin.demo-seed.json";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded seed '{name}' not found.");
            return JsonSerializer.Deserialize<SeedFile>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            }) ?? throw new InvalidOperationException("Demo seed file is empty.");
        }

        internal sealed record SeedFile(
            int Days,
            List<SeedAirport> Airports,
            List<SeedEquipment> Equipment,
            List<SeedMarket> Markets);

        internal sealed record SeedAirport(string Code, string Name, string City, string Tz);
        internal sealed record SeedEquipment(string Code, string Name, int RangeNm, int Capacity);
        internal sealed record SeedMarket(string Origin, string Destination, List<SeedFlight> Flights);

        internal sealed record SeedFlight(
            string Number,
            string Dep,
            string Arr,
            int ArrOffsetDays,
            string Equipment,
            [property: JsonPropertyName("return")] bool Return = false);
    }
}
