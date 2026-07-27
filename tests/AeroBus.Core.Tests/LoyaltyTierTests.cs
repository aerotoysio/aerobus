using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Services.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class LoyaltyTierTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Tiers_round_trip_search_and_order_by_rank()
    {
        var svc = new LoyaltyTiersService(new LoyaltyTiers(fx.Store));
        var company = DocumentForgeFixture.NewCompany();

        var gold = new LoyaltyTier { Id = Guid.NewGuid(), CompanyId = company, TierCode = "gold", TierName = "Gold", Description = "Top tier with lounge access", Rank = 3, Status = "Active" };
        var silver = new LoyaltyTier { Id = Guid.NewGuid(), CompanyId = company, TierCode = "SILVER", TierName = "Silver", Description = "Mid tier", Rank = 2, Status = "Active" };
        var blue = new LoyaltyTier { Id = Guid.NewGuid(), CompanyId = company, TierCode = "BLUE", TierName = "Blue", Description = "Entry tier", Rank = 1, Status = "Active" };

        await svc.SaveAsync(gold);
        await svc.SaveAsync(silver);
        await svc.SaveAsync(blue);

        // codes normalize to upper case at save
        Assert.Equal("GOLD", (await svc.GetByIdAsync(gold.Id))!.TierCode);

        // list is rank-ordered lowest -> highest
        var listed = await svc.ListByCompanyAsync(company, null, 1, 50);
        Assert.Equal(new[] { "BLUE", "SILVER", "GOLD" }, listed.Select(t => t.TierCode).ToArray());

        // search hits code, name and description
        Assert.Single(await svc.ListByCompanyAsync(company, "lounge", 1, 50));
        Assert.Single(await svc.ListByCompanyAsync(company, "silv", 1, 50));

        // a blank code is rejected
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(new LoyaltyTier { Id = Guid.NewGuid(), CompanyId = company }));

        // cleanup + confirm gone
        foreach (var t in listed) await svc.DeleteAsync(t.Id, Guid.Empty);
        Assert.Empty(await svc.ListByCompanyAsync(company, null, 1, 50));
    }
}
