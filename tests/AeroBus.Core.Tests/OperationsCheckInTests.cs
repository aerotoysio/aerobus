using AeroBus.Core.Model.Operations;
using AeroBus.Core.Repositories.Operations;
using AeroBus.Core.Repositories.Order;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Distribution;
using AeroBus.Core.Services.Operations;
using AeroBus.Core.Services.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Live DocumentForge round-trip for the DCS check-in surface: the flight-keyed
/// manifest query plus the Booked → CheckedIn → Boarded transitions and their
/// guards. Each test uses a fresh flightId so its manifest is naturally isolated.
/// The order roll-up is best-effort and no-ops here (no real order), which is the
/// intended behaviour when the commercial order isn't advanceable.
/// </summary>
[Collection("documentforge")]
public sealed class OperationsCheckInTests
{
    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private sealed class ThrowingRuleForge : IRuleForgeClient
    {
        public Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default) =>
            throw new HttpRequestException("unused");
        public Task<bool> HealthAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> RefreshAsync(CancellationToken ct = default) => Task.FromResult(false);
    }

    private readonly DocumentForgeFixture _fx;
    private readonly CheckIns _checkIns;
    private readonly CheckInService _svc;

    public OperationsCheckInTests(DocumentForgeFixture fx)
    {
        _fx = fx;
        _checkIns = new CheckIns(fx.Store);
        var publisher = EventsTestHelpers.Publisher(fx);
        var runner = new DecisionRunner(
            new ThrowingRuleForge(),
            new Opt<RuleForgeOptions>(new RuleForgeOptions { BaseUrl = "http://localhost:5050" }),
            new Opt<DecisionRunnerOptions>(new DecisionRunnerOptions()),
            NullLogger<DecisionRunner>.Instance);
        var orders = new Orders(fx.Store);
        var orderChange = new OrderChangeService(orders, new InventoryService(fx.Client, NullLogger<InventoryService>.Instance),
            runner, publisher, NullLogger<OrderChangeService>.Instance);
        _svc = new CheckInService(_checkIns, orders, orderChange, publisher, NullLogger<CheckInService>.Instance);
    }

    private async Task<(Guid company, Guid flight, Guid pax1, Guid pax2)> SeedManifestAsync()
    {
        var company = DocumentForgeFixture.NewCompany();
        var flight = Guid.NewGuid();
        var order = Guid.NewGuid();
        var pax1 = Guid.NewGuid();
        var pax2 = Guid.NewGuid();

        PassengerCheckIn Row(Guid pax, string first, string last) => new()
        {
            Id = Guid.NewGuid(), CompanyId = company, FlightId = flight, OrderId = order, PassengerId = pax,
            FirstName = first, LastName = last, PaxType = "ADT", BookedBucket = "Y",
            Status = CheckInStatus.Booked, Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
        };
        await _checkIns.SaveAsync(Row(pax1, "Ada", "Adams"));
        await _checkIns.SaveAsync(Row(pax2, "Ben", "Baker"));
        return (company, flight, pax1, pax2);
    }

    [Fact]
    public async Task Manifest_returns_booked_passengers_for_the_flight()
    {
        var (company, flight, _, _) = await SeedManifestAsync();
        var manifest = await _svc.GetManifestAsync(company, flight);
        Assert.Equal(2, manifest.Count);
        Assert.Equal("Adams", manifest[0].LastName); // ordered by last name
        Assert.All(manifest, r => Assert.Equal(CheckInStatus.Booked, r.Status));
    }

    [Fact]
    public async Task CheckIn_then_board_advances_status_and_assigns_seat_and_sequence()
    {
        var (company, flight, pax1, _) = await SeedManifestAsync();

        var checkedIn = await _svc.CheckInAsync(company, flight, pax1, seatRow: 12, seatColumn: "C");
        Assert.True(checkedIn.Success);
        Assert.Equal(CheckInStatus.CheckedIn, checkedIn.CheckIn!.Status);
        Assert.Equal(12, checkedIn.CheckIn.SeatRow);
        Assert.Equal("C", checkedIn.CheckIn.SeatColumn);
        Assert.NotNull(checkedIn.CheckIn.CheckedInAt);

        var boarded = await _svc.BoardAsync(company, flight, pax1);
        Assert.True(boarded.Success);
        Assert.Equal(CheckInStatus.Boarded, boarded.CheckIn!.Status);
        Assert.Equal(1, boarded.CheckIn.BoardingSequence);
    }

    [Fact]
    public async Task Boarding_before_check_in_is_rejected()
    {
        var (company, flight, _, pax2) = await SeedManifestAsync();
        var result = await _svc.BoardAsync(company, flight, pax2);
        Assert.False(result.Success);
        Assert.Equal("invalidState", result.Code);
    }

    [Fact]
    public async Task Unknown_passenger_is_not_found()
    {
        var (company, flight, _, _) = await SeedManifestAsync();
        var result = await _svc.CheckInAsync(company, flight, Guid.NewGuid(), null, null);
        Assert.Equal("notFound", result.Code);
    }

    [Fact]
    public async Task BoardAll_boards_every_checked_in_passenger()
    {
        var (company, flight, pax1, pax2) = await SeedManifestAsync();
        await _svc.CheckInAsync(company, flight, pax1, null, null);
        await _svc.CheckInAsync(company, flight, pax2, null, null);

        var boarded = await _svc.BoardAllAsync(company, flight);
        Assert.Equal(2, boarded);

        var manifest = await _svc.GetManifestAsync(company, flight);
        Assert.All(manifest, r => Assert.Equal(CheckInStatus.Boarded, r.Status));
    }
}
