using AeroBus.Core.Common.Cache;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Services.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroBus.Core.Tests;

file sealed class DemoSeedUtcTz : ITimeZoneResolver
{
    public TimeZoneInfo? Resolve(string stationCode) => TimeZoneInfo.Utc;
}

/// <summary>
/// Live-DF round-trip for the demo-airline seed: manifest counts come from the
/// embedded seed file, each section is idempotent, and the flights section builds
/// real flights (via the flight builder) and flips the demo.seeded config. A fresh
/// company id per test keeps runs isolated.
/// </summary>
[Collection("documentforge")]
public sealed class DemoSeedTests
{
    private readonly DocumentForgeFixture _fx;
    private readonly DemoSeedService _svc;

    public DemoSeedTests(DocumentForgeFixture fx)
    {
        _fx = fx;
        var builder = new FlightBuilder(
            new Schedules(fx.Store), new Flights(fx.Store), new Layouts(fx.Store),
            new AeroBus.Core.Repositories.Stock.FlightInventories(fx.Store),
            new DemoSeedUtcTz(),
            EventsTestHelpers.Publisher(fx));
        _svc = new DemoSeedService(
            fx.Store, new Companies(fx.Store), new CompanyConfigs(fx.Store),
            builder, NullLogger<DemoSeedService>.Instance);
    }

    private async Task<Company> SeedCompanyAsync()
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Demo Seed Air",
            Slug = $"demo-seed-{Guid.NewGuid():N}",
            Status = "Active",
            Designator = "DS",
            OperatingCurrency = "AED",
            DefaultExpectedLoadFactor = 0.8m,
            Created = DateTime.UtcNow,
        };
        await new Companies(_fx.Store).SaveAsync(company);
        return company;
    }

    [Fact]
    public async Task Manifest_reports_planned_counts_and_not_seeded_for_a_fresh_org()
    {
        var company = await SeedCompanyAsync();
        var manifest = await _svc.GetManifestAsync(company.Id);

        Assert.False(manifest.Seeded);
        var byKey = manifest.Sections.ToDictionary(s => s.Key);
        Assert.Equal(8, byKey["airports"].Planned);
        Assert.Equal(3, byKey["equipment"].Planned);
        Assert.Equal(16, byKey["markets"].Planned);
        Assert.Equal(16 * 14, byKey["flights"].Planned);
        Assert.All(manifest.Sections, s => Assert.Equal(0, s.Existing));
    }

    [Fact]
    public async Task Sections_seed_in_order_idempotently_and_flights_flip_seeded()
    {
        var company = await SeedCompanyAsync();

        var airports = await _svc.SeedSectionAsync(company.Id, "airports");
        Assert.Equal(8, airports.Created);
        var equipment = await _svc.SeedSectionAsync(company.Id, "equipment");
        Assert.Equal(3, equipment.Created);
        var markets = await _svc.SeedSectionAsync(company.Id, "markets");
        Assert.Equal(16, markets.Created);

        // Schedules fly under the org's own designator.
        var schedules = await new Schedules(_fx.Store).GetByCompanyAsync(company.Id);
        Assert.All(schedules, s => Assert.Equal("DS", s.CarrierCode));

        var flights = await _svc.SeedSectionAsync(company.Id, "flights");
        Assert.True(flights.Created > 0, "flights section should build flights");
        Assert.Equal(16 * 14, flights.Total);

        var manifest = await _svc.GetManifestAsync(company.Id);
        Assert.True(manifest.Seeded);

        // Idempotency: nothing new on a second pass of any section.
        Assert.Equal(0, (await _svc.SeedSectionAsync(company.Id, "airports")).Created);
        Assert.Equal(0, (await _svc.SeedSectionAsync(company.Id, "markets")).Created);
        Assert.Equal(0, (await _svc.SeedSectionAsync(company.Id, "flights")).Created);
    }

    [Fact]
    public async Task Unknown_section_is_rejected()
    {
        var company = await SeedCompanyAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SeedSectionAsync(company.Id, "nonsense"));
    }
}
