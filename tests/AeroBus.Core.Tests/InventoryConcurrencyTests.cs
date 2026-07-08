using AeroBus.Core.Model.Stock;
using AeroBus.Core.Repositories.Stock;
using AeroBus.Core.Services.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// The headline Phase-5 correctness test: under M concurrent SellAsync calls for a
/// flight with only N seats, EXACTLY N succeed and the rest get "soldOut" — never
/// an oversell, never a negative counter. This is the whole reason the inventory
/// counters are top-level scalars mutated through the DocumentForge conditional
/// update (compare-and-set under the engine write lock).
/// </summary>
[Collection("documentforge")]
public class InventoryConcurrencyTests(DocumentForgeFixture fx)
{
    private InventoryService NewService() =>
        new(fx.Client, NullLogger<InventoryService>.Instance);

    private async Task<FlightInventory> SeedInventoryAsync(Guid companyId, Guid flightId, string bucket, int capacity)
    {
        var inventories = new FlightInventories(fx.Store);
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
        await inventories.SaveAsync(inv);
        return inv;
    }

    [Fact]
    public async Task Concurrent_sells_never_oversell_exactly_capacity_succeeds()
    {
        var company = DocumentForgeFixture.NewCompany();
        var flightId = Guid.NewGuid();
        const string bucket = "Y";
        const int capacity = 10;   // N seats
        const int contenders = 25; // M > N concurrent single-seat sells

        var seed = await SeedInventoryAsync(company, flightId, bucket, capacity);
        var svc = NewService();

        // Fire M single-seat sells at once.
        var tasks = Enumerable.Range(0, contenders)
            .Select(_ => svc.SellAsync(company, flightId, bucket, 1))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var succeeded = results.Count(r => r.Success);
        var soldOut = results.Count(r => !r.Success && r.Reason == "soldOut");

        Assert.Equal(capacity, succeeded);                 // exactly N win
        Assert.Equal(contenders - capacity, soldOut);      // the rest are sold out
        Assert.All(results.Where(r => !r.Success), r => Assert.Equal("soldOut", r.Reason));

        // Final counters: Available == 0, Sold == N, and never negative.
        var inventories = new FlightInventories(fx.Store);
        var final = await inventories.GetByIdAsync(seed.Id);
        Assert.NotNull(final);
        Assert.Equal(0, final!.Available);
        Assert.Equal(capacity, final.Sold);
        Assert.True(final.Available >= 0);
        Assert.True(final.Sold <= final.Capacity);

        await inventories.DeleteAsync(seed.Id, Guid.Empty);
    }

    [Fact]
    public async Task Sell_then_release_round_trips_the_counters()
    {
        var company = DocumentForgeFixture.NewCompany();
        var flightId = Guid.NewGuid();
        const string bucket = "ALL";

        var seed = await SeedInventoryAsync(company, flightId, bucket, 5);
        var svc = NewService();

        Assert.True((await svc.SellAsync(company, flightId, bucket, 2)).Success);

        var inventories = new FlightInventories(fx.Store);
        var afterSell = await inventories.GetByIdAsync(seed.Id);
        Assert.Equal(3, afterSell!.Available);
        Assert.Equal(2, afterSell.Sold);

        Assert.True((await svc.ReleaseAsync(company, flightId, bucket, 2)).Success);
        var afterRelease = await inventories.GetByIdAsync(seed.Id);
        Assert.Equal(5, afterRelease!.Available);
        Assert.Equal(0, afterRelease.Sold);

        await inventories.DeleteAsync(seed.Id, Guid.Empty);
    }

    [Fact]
    public async Task Release_beyond_sold_is_idempotent_no_negative_sold()
    {
        var company = DocumentForgeFixture.NewCompany();
        var flightId = Guid.NewGuid();
        const string bucket = "ALL";

        var seed = await SeedInventoryAsync(company, flightId, bucket, 4);
        var svc = NewService();

        await svc.SellAsync(company, flightId, bucket, 1);
        // First release returns the seat; a second (double) release is a no-op.
        Assert.True((await svc.ReleaseAsync(company, flightId, bucket, 1)).Success);
        Assert.True((await svc.ReleaseAsync(company, flightId, bucket, 1)).Success);

        var inventories = new FlightInventories(fx.Store);
        var final = await inventories.GetByIdAsync(seed.Id);
        Assert.Equal(0, final!.Sold);          // not negative
        Assert.Equal(4, final.Available);      // not over capacity

        await inventories.DeleteAsync(seed.Id, Guid.Empty);
    }

    [Fact]
    public async Task Sell_against_missing_inventory_returns_noInventory()
    {
        var company = DocumentForgeFixture.NewCompany();
        var svc = NewService();
        var result = await svc.SellAsync(company, Guid.NewGuid(), "Y", 1);
        Assert.False(result.Success);
        Assert.Equal("noInventory", result.Reason);
    }

    [Fact]
    public async Task Partial_shortfall_reports_insufficient_not_soldOut()
    {
        var company = DocumentForgeFixture.NewCompany();
        var flightId = Guid.NewGuid();
        const string bucket = "Y";

        var seed = await SeedInventoryAsync(company, flightId, bucket, 3);
        var svc = NewService();

        // Ask for 5 when only 3 remain: guard fails, Available > 0 → insufficient.
        var result = await svc.SellAsync(company, flightId, bucket, 5);
        Assert.False(result.Success);
        Assert.Equal("insufficient", result.Reason);

        var inventories = new FlightInventories(fx.Store);
        var final = await inventories.GetByIdAsync(seed.Id);
        Assert.Equal(3, final!.Available); // untouched
        Assert.Equal(0, final.Sold);

        await inventories.DeleteAsync(seed.Id, Guid.Empty);
    }
}
