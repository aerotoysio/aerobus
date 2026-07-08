using AeroBus.Core.Model.Admin;
using AeroBus.Core.Model.Customer;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Model.Shopping;
using AeroBus.Core.Model.Stock;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Distribution;
using AeroBus.Core.Repositories.Order;
using AeroBus.Core.Repositories.Stock;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Distribution;
using AeroBus.Core.Services.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// End-to-end order lifecycle over live DocumentForge: seed a company + a shopped
/// offer + flight inventory directly (no /offer/shop needed), then drive
/// OrderCreateService → retrieve → cancel, asserting the seat inventory is
/// decremented on create and restored on cancel and that the order status
/// transitions through the state machine. RuleForge is unavailable in these tests
/// (a throwing stub client), which — for the order decision points' default-Allow
/// failure mode — must not block a legitimate order.
/// </summary>
[Collection("documentforge")]
public class OrderLifecycleTests(DocumentForgeFixture fx)
{
    // RuleForge "down": a client that always throws. Order points default to Allow,
    // so create/change proceed on the local state machine alone.
    private sealed class ThrowingRuleForgeClient : IRuleForgeClient
    {
        public Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default) =>
            throw new HttpRequestException("RuleForge unavailable (test).");

        public Task<bool> HealthAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> RefreshAsync(CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private DecisionRunner Runner() =>
        new(new ThrowingRuleForgeClient(),
            new Opt<RuleForgeOptions>(new RuleForgeOptions { BaseUrl = "http://localhost:5050" }),
            new Opt<DecisionRunnerOptions>(new DecisionRunnerOptions()),
            NullLogger<DecisionRunner>.Instance);

    private InventoryService Inventory() => new(fx.Client, NullLogger<InventoryService>.Instance);

    private OrderCreateService CreateService() =>
        new(new Companies(fx.Store), new Orders(fx.Store), new Offers(fx.Store),
            Inventory(), Runner(), EventsTestHelpers.Publisher(fx), NullLogger<OrderCreateService>.Instance);

    private OrderChangeService ChangeService() =>
        new(new Orders(fx.Store), Inventory(), Runner(), EventsTestHelpers.Publisher(fx), NullLogger<OrderChangeService>.Instance);

    private OrderRetrieveService RetrieveService() =>
        new(new Companies(fx.Store), new Orders(fx.Store));

    private async Task<Company> SeedCompanyAsync()
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Test Air",
            Slug = $"test-air-{Guid.NewGuid():N}",
            Status = "Active",
            Designator = "TA",
            AccountingCode = "999",
            OperatingCurrency = "AED",
            Created = DateTime.UtcNow,
        };
        await new Companies(fx.Store).SaveAsync(company);
        return company;
    }

    private async Task<FlightInventory> SeedInventoryAsync(Guid companyId, Guid flightId, string bucket, int capacity)
    {
        var inv = new FlightInventory
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            FlightId = flightId,
            Bucket = bucket,
            Capacity = capacity,
            Sold = 0,
            Available = capacity,
            Created = DateTime.UtcNow,
            Status = "Active",
        };
        await new FlightInventories(fx.Store).SaveAsync(inv);
        return inv;
    }

    // A shopped offer with one solution (one flight leg) and one priced bundle.
    private async Task<Offer> SeedOfferAsync(Guid companyId, Guid flightId, string? cabin, Guid bundleId)
    {
        var solution = new FlightSolution
        {
            Id = Guid.NewGuid(),
            Cabin = cabin,
            Flights = new List<FlightSegment>
            {
                new() { Id = Guid.NewGuid(), FlightRef = flightId.ToString(), MarketingCarrier = "TA" },
            },
            Bundles = new List<ShopBundle>
            {
                new()
                {
                    Id = bundleId,
                    BundleCode = "LITE",
                    Name = "Lite",
                    Description = "Lite fare",
                    Price = new BundlePrice { Currency = "AED", Base = 400m, Taxes = 100m, Total = 500m },
                },
            },
        };

        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            SearchId = Guid.NewGuid(),
            Channel = "web",
            Currency = "AED",
            OriginDestinations = new List<OriginDestinationResponse>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Origin = "DXB",
                    Destination = "LHR",
                    DepartureDate = DateTime.UtcNow.Date.AddDays(30),
                    FlightSolutions = new List<FlightSolution> { solution },
                },
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            Created = DateTime.UtcNow,
            Status = "Shopped",
        };
        await new Offers(fx.Store).SaveAsync(offer);
        return offer;
    }

    private static List<Passenger> TwoPassengers() => new()
    {
        new Passenger { Id = Guid.NewGuid(), PaxType = "ADT", FirstName = "Ada", LastName = "Lovelace" },
        new Passenger { Id = Guid.NewGuid(), PaxType = "ADT", FirstName = "Alan", LastName = "Turing" },
    };

    [Fact]
    public async Task Create_decrements_inventory_and_confirms_then_cancel_restores_and_cancels()
    {
        var company = await SeedCompanyAsync();
        var flightId = Guid.NewGuid();
        const string bucket = "ALL";              // solution has no cabin → "ALL"
        const int capacity = 20;

        var inv = await SeedInventoryAsync(company.Id, flightId, bucket, capacity);
        var bundleId = Guid.NewGuid();
        var offer = await SeedOfferAsync(company.Id, flightId, cabin: null, bundleId);

        var inventories = new FlightInventories(fx.Store);
        var orders = new Orders(fx.Store);

        // ── create ────────────────────────────────────────────────────────────
        var passengers = TwoPassengers();
        var createReq = new OrderCreateRequest
        {
            Channel = "web",
            OfferId = offer.Id,
            SolutionId = null,
            BundleId = bundleId,
            Passengers = passengers,
            Payment = new PaymentRequest { Provider = "Manual", Method = "Card", Currency = "AED", Amount = 500m },
        };

        var created = await CreateService().Create(createReq, company.Id);
        Assert.True(created.Success, created.Message);
        Assert.NotNull(created.Order?.Order);
        var order = created.Order!.Order!;
        Assert.Equal("Confirmed", order.Status);
        Assert.False(string.IsNullOrWhiteSpace(order.OrderId));

        // Inventory decremented by seats (2 passengers).
        var afterCreate = await inventories.GetByIdAsync(inv.Id);
        Assert.Equal(capacity - 2, afterCreate!.Available);
        Assert.Equal(2, afterCreate.Sold);

        // ── retrieve (by public OrderId + last name gate) ──────────────────────
        var retrieved = await RetrieveService().Retrieve(
            new OrderRetrieveRequest { OrderId = order.OrderId, LastName = "Lovelace" }, company.Id);
        Assert.True(retrieved.Success);
        Assert.Single(retrieved.Orders);
        Assert.Equal(order.Id, retrieved.Orders[0].Order!.Id);

        // Wrong last name → not found.
        var wrongName = await RetrieveService().Retrieve(
            new OrderRetrieveRequest { OrderId = order.OrderId, LastName = "Nobody" }, company.Id);
        Assert.False(wrongName.Success);

        // ── cancel (releases inventory) ────────────────────────────────────────
        var cancelled = await ChangeService().ChangeStatus(
            new OrderChangeRequest { OrderId = order.Id, Action = "Cancel", Reason = "test" }, company.Id);
        Assert.True(cancelled.Success, cancelled.ErrorMessage);
        Assert.Equal("Confirmed", cancelled.PreviousStatus);
        Assert.Equal("Cancelled", cancelled.NewStatus);
        Assert.True(cancelled.InventoryReleased);

        // Inventory restored to full capacity.
        var afterCancel = await inventories.GetByIdAsync(inv.Id);
        Assert.Equal(capacity, afterCancel!.Available);
        Assert.Equal(0, afterCancel.Sold);

        // Order persisted as Cancelled.
        var persisted = await orders.GetByIdAsync(order.Id);
        Assert.Equal("Cancelled", persisted!.Status);

        // ── cleanup ────────────────────────────────────────────────────────────
        await orders.DeleteAsync(order.Id);
        await inventories.DeleteAsync(inv.Id, Guid.Empty);
        await new Offers(fx.Store).DeleteAsync(offer.Id);
        await new Companies(fx.Store).DeleteAsync(company.Id);
    }

    [Fact]
    public async Task Create_on_sold_out_flight_fails_with_soldOut_and_no_order()
    {
        var company = await SeedCompanyAsync();
        var flightId = Guid.NewGuid();
        const string bucket = "ALL";

        // Only 1 seat, but 2 passengers → guard fails, no order confirmed.
        var inv = await SeedInventoryAsync(company.Id, flightId, bucket, 1);
        var bundleId = Guid.NewGuid();
        var offer = await SeedOfferAsync(company.Id, flightId, cabin: null, bundleId);

        var createReq = new OrderCreateRequest
        {
            Channel = "web",
            OfferId = offer.Id,
            BundleId = bundleId,
            Passengers = TwoPassengers(),
            Payment = new PaymentRequest { Currency = "AED", Amount = 500m },
        };

        var result = await CreateService().Create(createReq, company.Id);
        Assert.False(result.Success);
        Assert.Equal("insufficient", result.Reason); // 1 available, need 2

        // Inventory untouched (compensating release left it whole).
        var inventories = new FlightInventories(fx.Store);
        var after = await inventories.GetByIdAsync(inv.Id);
        Assert.Equal(1, after!.Available);
        Assert.Equal(0, after.Sold);

        // No confirmed order persisted for this company.
        var orders = await new Orders(fx.Store).GetByCompanyAsync(company.Id);
        Assert.Empty(orders);

        await inventories.DeleteAsync(inv.Id, Guid.Empty);
        await new Offers(fx.Store).DeleteAsync(offer.Id);
        await new Companies(fx.Store).DeleteAsync(company.Id);
    }

    [Fact]
    public async Task Invalid_transition_is_rejected_by_the_state_machine()
    {
        var company = await SeedCompanyAsync();
        var flightId = Guid.NewGuid();
        var inv = await SeedInventoryAsync(company.Id, flightId, "ALL", 10);
        var bundleId = Guid.NewGuid();
        var offer = await SeedOfferAsync(company.Id, flightId, cabin: null, bundleId);

        var created = await CreateService().Create(new OrderCreateRequest
        {
            Channel = "web",
            OfferId = offer.Id,
            BundleId = bundleId,
            Passengers = TwoPassengers(),
        }, company.Id);
        Assert.True(created.Success, created.Message);
        var order = created.Order!.Order!;

        // Confirmed → Board is not a legal transition.
        var bad = await ChangeService().ChangeStatus(
            new OrderChangeRequest { OrderId = order.Id, Action = "Board" }, company.Id);
        Assert.False(bad.Success);
        Assert.Contains("Cannot perform", bad.ErrorMessage);

        // Inventory unchanged by the rejected change.
        var after = await new FlightInventories(fx.Store).GetByIdAsync(inv.Id);
        Assert.Equal(8, after!.Available);

        await new Orders(fx.Store).DeleteAsync(order.Id);
        await new FlightInventories(fx.Store).DeleteAsync(inv.Id, Guid.Empty);
        await new Offers(fx.Store).DeleteAsync(offer.Id);
        await new Companies(fx.Store).DeleteAsync(company.Id);
    }
}
