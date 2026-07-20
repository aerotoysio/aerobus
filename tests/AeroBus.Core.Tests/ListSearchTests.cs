using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Server-side list search (DocumentForge LIKE, case-insensitive): airports
/// match on code/name/city, equipment on type code/name, always scoped to the
/// company and combinable with paging.
/// </summary>
[Collection("documentforge")]
public class ListSearchTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Airport_search_matches_code_name_and_city_case_insensitively()
    {
        var company = DocumentForgeFixture.NewCompany();
        var other = DocumentForgeFixture.NewCompany();
        var repo = new Airports(fx.Store);

        foreach (var (code, name, city) in new[]
                 {
                     ("DXB", "Dubai International", "Dubai"),
                     ("LHR", "London Heathrow", "London"),
                     ("LGW", "London Gatwick", "London"),
                 })
            await repo.SaveAsync(new Airport
            {
                Id = Guid.NewGuid(), Code = code, Name = name, City = city,
                Status = "Active", CompanyId = company,
            });

        // Same code under ANOTHER company must never leak into results.
        await repo.SaveAsync(new Airport
        {
            Id = Guid.NewGuid(), Code = "LHR", Name = "Other's Heathrow", City = "London",
            Status = "Active", CompanyId = other,
        });

        var byCity = await repo.ListByCompanyAsync(company, "london", 1, 50);
        Assert.Equal(2, byCity.Count);
        Assert.All(byCity, a => Assert.Equal(company, a.CompanyId));

        var byCode = await repo.ListByCompanyAsync(company, "lhr", 1, 50);
        Assert.Equal("LHR", Assert.Single(byCode).Code);

        var byName = await repo.ListByCompanyAsync(company, "gatw", 1, 50);
        Assert.Equal("LGW", Assert.Single(byName).Code);

        var none = await repo.ListByCompanyAsync(company, "zzz-no-match", 1, 50);
        Assert.Empty(none);

        var all = await repo.ListByCompanyAsync(company, search: null, 1, 50);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Equipment_search_matches_type_code_and_name()
    {
        var company = DocumentForgeFixture.NewCompany();
        var repo = new EquipmentRepo(fx.Store);

        foreach (var (code, name) in new[] { ("320", "Airbus A320"), ("77W", "Boeing 777-300ER"), ("388", "Airbus A380") })
            await repo.SaveAsync(new Equipment
            {
                Id = Guid.NewGuid(), EquipmentCode = code, Name = name,
                Status = "Active", CompanyId = company,
            });

        var airbus = await repo.ListByCompanyAsync(company, status: null, "airbus", 1, 50);
        Assert.Equal(2, airbus.Count);

        var byCode = await repo.ListByCompanyAsync(company, status: null, "77w", 1, 50);
        Assert.Equal("77W", Assert.Single(byCode).EquipmentCode);

        // Search composes with the status filter.
        var activeAirbus = await repo.ListByCompanyAsync(company, "Active", "airbus", 1, 50);
        Assert.Equal(2, activeAirbus.Count);
    }
}
