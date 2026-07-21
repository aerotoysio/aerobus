using AeroBus.Core.Repositories.Order;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// The order list: company-scoped, newest first, searchable by public order id,
/// composable with a status filter.
/// </summary>
[Collection("documentforge")]
public class OrderListTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Lists_newest_first_scoped_to_company_with_search_and_status()
    {
        var company = DocumentForgeFixture.NewCompany();
        var other = DocumentForgeFixture.NewCompany();
        var repo = new Orders(fx.Store);

        foreach (var (orderId, status, created) in new[]
                 {
                     ("VF0AAA0001", "Confirmed", DateTime.UtcNow.AddMinutes(-30)),
                     ("VF0BBB0002", "Cancelled", DateTime.UtcNow.AddMinutes(-20)),
                     ("VF0CCC0003", "Confirmed", DateTime.UtcNow.AddMinutes(-10)),
                 })
            await repo.SaveAsync(new Core.Model.Order.Order
            {
                Id = Guid.NewGuid(), OrderId = orderId, Status = status,
                CompanyId = company, Created = created,
            });

        await repo.SaveAsync(new Core.Model.Order.Order
        {
            Id = Guid.NewGuid(), OrderId = "XX0OTHER01", Status = "Confirmed",
            CompanyId = other, Created = DateTime.UtcNow,
        });

        var all = await repo.ListByCompanyAsync(company, status: null, search: null, 1, 50);
        Assert.Equal(3, all.Count);
        Assert.Equal("VF0CCC0003", all[0].OrderId); // newest first
        Assert.All(all, o => Assert.Equal(company, o.CompanyId));

        var confirmed = await repo.ListByCompanyAsync(company, "Confirmed", null, 1, 50);
        Assert.Equal(2, confirmed.Count);

        var byId = await repo.ListByCompanyAsync(company, null, "bbb", 1, 50);
        Assert.Equal("VF0BBB0002", Assert.Single(byId).OrderId);
    }
}
