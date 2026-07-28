using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Services.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class RightToFlyTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Rights_to_fly_round_trip_with_rule_attachments_and_policies()
    {
        var svc = new RightToFlyService(new RightsToFly(fx.Store));
        var company = DocumentForgeFixture.NewCompany();

        var saver = new RightToFly
        {
            Id = Guid.NewGuid(), CompanyId = company, RtfCode = "saver", RtfName = "Saver",
            Description = "The essentials", Rank = 1, Status = "Active",
            PricingRuleId = "rule-shop-bundles",
            PolicyIds = ["policy-baggage"],
        };
        var business = new RightToFly
        {
            Id = Guid.NewGuid(), CompanyId = company, RtfCode = "BUSINESS", RtfName = "Business",
            Description = "Everything included", Rank = 3, Status = "Active",
            EligibilityRuleId = "rule-business-eligibility",
            PolicyIds = ["policy-baggage", "policy-lounge"],
        };

        await svc.SaveAsync(saver);
        await svc.SaveAsync(business);

        // codes normalize; attachments round-trip
        var gotSaver = await svc.GetByIdAsync(saver.Id);
        Assert.Equal("SAVER", gotSaver!.RtfCode);
        Assert.Equal("rule-shop-bundles", gotSaver.PricingRuleId);
        Assert.Equal(["policy-baggage"], gotSaver.PolicyIds);

        var gotBusiness = await svc.GetByIdAsync(business.Id);
        Assert.Equal(["policy-baggage", "policy-lounge"], gotBusiness!.PolicyIds);

        // rank-ordered list + search over code/name/description
        var listed = await svc.ListByCompanyAsync(company, null, 1, 50);
        Assert.Equal(new[] { "SAVER", "BUSINESS" }, listed.Select(r => r.RtfCode).ToArray());
        Assert.Single(await svc.ListByCompanyAsync(company, "essentials", 1, 50));

        // updating policy connections sticks
        await svc.SaveAsync(gotSaver with { PolicyIds = ["policy-baggage", "policy-lounge"] });
        Assert.Equal(2, (await svc.GetByIdAsync(saver.Id))!.PolicyIds!.Count);

        // blank code rejected
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(new RightToFly { Id = Guid.NewGuid(), CompanyId = company }));

        foreach (var r in listed) await svc.DeleteAsync(r.Id, Guid.Empty);
        Assert.Empty(await svc.ListByCompanyAsync(company, null, 1, 50));
    }
}
