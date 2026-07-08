using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class ReferenceDataTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Continent_country_region_airport_round_trip_through_the_generic_repository()
    {
        var continents = new Continents(fx.Store);
        var countries = new Countries(fx.Store);
        var regions = new Regions(fx.Store);
        var airports = new Airports(fx.Store);

        var company = DocumentForgeFixture.NewCompany();

        var continent = new Continent { Id = Guid.NewGuid(), CompanyId = company, Name = "Oceania", Code = "OC", Status = "Active", Created = DateTime.UtcNow };
        var country = new Country { Id = Guid.NewGuid(), CompanyId = company, Name = "Australia", Code = "AU", ContinentId = continent.Id, Status = "Active", Created = DateTime.UtcNow };
        var region = new Region { Id = Guid.NewGuid(), CompanyId = company, Name = "Queensland", Code = "QLD", CountryId = country.Id, Status = "Active", Created = DateTime.UtcNow };
        var airport = new Airport { Id = Guid.NewGuid(), CompanyId = company, Code = "BNE", Name = "Brisbane", City = "Brisbane", CountryId = country.Id, RegionId = region.Id, TimeZoneId = "Australia/Brisbane", Status = "Active", Created = DateTime.UtcNow };

        await continents.SaveAsync(continent);
        await countries.SaveAsync(country);
        await regions.SaveAsync(region);
        await airports.SaveAsync(airport);

        // get-by-id (inherited from DocumentRepository<T>)
        Assert.Equal("Oceania", (await continents.GetByIdAsync(continent.Id))!.Name);
        Assert.Equal("AU", (await countries.GetByIdAsync(country.Id))!.Code);
        Assert.Equal("QLD", (await regions.GetByIdAsync(region.Id))!.Code);

        var gotAirport = await airports.GetByIdAsync(airport.Id);
        Assert.NotNull(gotAirport);
        Assert.Equal("BNE", gotAirport!.Code);
        Assert.Equal(country.Id, gotAirport.CountryId);
        Assert.Equal(region.Id, gotAirport.RegionId);

        // bespoke cross-aggregate queries
        Assert.Contains(await countries.GetByContinentAsync(continent.Id), c => c.Id == country.Id);
        Assert.Contains(await regions.GetByCountryAsync(country.Id), r => r.Id == region.Id);

        // company scoping (inherited)
        Assert.Contains(await airports.GetByCompanyAsync(company), a => a.Id == airport.Id);

        // update round-trips
        await airports.SaveAsync(airport with { City = "Brisbane City" });
        Assert.Equal("Brisbane City", (await airports.GetByIdAsync(airport.Id))!.City);

        // cleanup + confirm gone
        await airports.DeleteAsync(airport.Id, Guid.Empty);
        await regions.DeleteAsync(region.Id, Guid.Empty);
        await countries.DeleteAsync(country.Id, Guid.Empty);
        await continents.DeleteAsync(continent.Id, Guid.Empty);

        Assert.Null(await airports.GetByIdAsync(airport.Id));
        Assert.Null(await continents.GetByIdAsync(continent.Id));
    }
}
