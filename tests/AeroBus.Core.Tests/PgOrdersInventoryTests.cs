using AeroBus.Core.Model.Stock;
using AeroBus.Core.Repositories.Order;
using AeroBus.Core.Repositories.Stock;
using AeroBus.Core.Services.Stock;
using Xunit;
using OrderModel = AeroBus.Core.Model.Order.Order;

namespace AeroBus.Core.Tests;

[Collection("postgres")]
public class PgOrdersInventoryTests(PostgresFixture fx)
{
    [Fact]
    public async Task Orders_round_trip_list_filters_and_search_on_postgres()
    {
        var repo = new PgOrders(fx.Db);
        var company = Guid.NewGuid();
        var profile = Guid.NewGuid();

        var o1 = new OrderModel
        {
            Id = Guid.NewGuid(), CompanyId = company, OrderId = "VF0TEST01", Status = "Confirmed",
            ProfileId = profile, Channel = "web", Created = DateTime.UtcNow.AddMinutes(-2), Updated = DateTime.UtcNow,
        };
        var o2 = new OrderModel
        {
            Id = Guid.NewGuid(), CompanyId = company, OrderId = "VF0TEST02", Status = "Cancelled",
            Channel = "agent", Created = DateTime.UtcNow.AddMinutes(-1), Updated = DateTime.UtcNow,
        };
        await repo.SaveAsync(o1);
        await repo.SaveAsync(o2);

        Assert.Equal("Confirmed", (await repo.GetByOrderIdAsync("VF0TEST01"))!.Status);

        var all = await repo.ListByCompanyAsync(company, null, null, null, 1, 10);
        Assert.Equal(new[] { "VF0TEST02", "VF0TEST01" }, all.Select(o => o.OrderId).ToArray()); // newest first

        Assert.Single(await repo.ListByCompanyAsync(company, "Cancelled", null, null, 1, 10));
        Assert.Single(await repo.ListByCompanyAsync(company, null, "TEST01", null, 1, 10));
        Assert.Single(await repo.ListByCompanyAsync(company, null, null, profile, 1, 10));
        Assert.Empty(await repo.ListByCompanyAsync(Guid.NewGuid(), null, null, null, 1, 10)); // other org

        foreach (var o in all) await repo.DeleteAsync(o.Id);
    }

    [Fact]
    public async Task Inventory_sell_and_release_are_atomic_guarded_updates()
    {
        var inventories = new PgFlightInventories(fx.Db);
        var svc = new PgInventoryService(fx.Db);
        var company = Guid.NewGuid();
        var flight = Guid.NewGuid();

        await inventories.SaveAsync(new FlightInventory
        {
            Id = Guid.NewGuid(), CompanyId = company, FlightId = flight,
            Bucket = "Y", Capacity = 3, Available = 3, Sold = 0, Status = "Open",
        });

        // sell within availability
        Assert.True((await svc.SellAsync(company, flight, "Y", 2)).Success);
        // insufficient: 1 left, want 2
        Assert.Equal("insufficient", (await svc.SellAsync(company, flight, "Y", 2)).Reason);
        // sell the last one, then sold out
        Assert.True((await svc.SellAsync(company, flight, "Y", 1)).Success);
        Assert.Equal("soldOut", (await svc.SellAsync(company, flight, "Y", 1)).Reason);
        // unknown bucket
        Assert.Equal("noInventory", (await svc.SellAsync(company, flight, "J", 1)).Reason);

        // release two, counters recover
        Assert.True((await svc.ReleaseAsync(company, flight, "Y", 2)).Success);
        var rows = await inventories.GetByFlightAsync(flight);
        Assert.Equal(2, rows.Single().Available);
        Assert.Equal(1, rows.Single().Sold);
        // over-release guarded
        Assert.Equal("insufficient", (await svc.ReleaseAsync(company, flight, "Y", 5)).Reason);
    }
}
