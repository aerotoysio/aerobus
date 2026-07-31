using AeroBus.Core.Model.Customer;
using AeroBus.Core.Repositories.Customer;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("postgres")]
public class PgCustomersTests(PostgresFixture fx)
{
    [Fact]
    public async Task Customers_round_trip_identity_lookups_and_search_on_postgres()
    {
        var repo = new PgCustomers(fx.Db);
        var company = Guid.NewGuid();

        var ada = new Customer
        {
            Id = Guid.NewGuid(), CompanyId = company, CustomerNumber = "CU00000001",
            FirstName = "Ada", LastName = "Lovelace", Email = "Ada@Example.Test",
            Phone = "+971 555 0101", LoyaltyProgram = "GOLD", Status = "Active",
            Passports = [new Passport { Id = Guid.NewGuid(), CountryCode = "GB", PassportNumber = "P123" }],
        };
        await repo.SaveAsync(ada);

        // aggregate fidelity through the jsonb doc
        var got = await repo.GetByIdAsync(ada.Id);
        Assert.Equal("Ada", got!.FirstName);
        Assert.Single(got.Passports!);
        Assert.Equal("GB", got.Passports![0].CountryCode);
        Assert.NotNull(got.Created);

        // identity lookups are case-insensitive
        Assert.NotNull(await repo.FindByEmailAsync(company, "ada@example.test"));
        Assert.NotNull(await repo.FindByPhoneAndLastNameAsync(company, "+971 555 0101", "LOVELACE"));
        Assert.Null(await repo.FindByEmailAsync(Guid.NewGuid(), "ada@example.test")); // other org

        // search + filters
        var listed = await repo.ListByCompanyAsync(company, "GOLD", null, "love", 1, 10);
        Assert.Single(listed);
        Assert.Empty(await repo.ListByCompanyAsync(company, "SILVER", null, null, 1, 10));

        // number lookup + update preserves created
        Assert.NotNull(await repo.GetByNumberAsync("CU00000001"));
        var created = got.Created;
        await repo.SaveAsync(got with { LoyaltyProgram = "SILVER" });
        var updated = await repo.GetByIdAsync(ada.Id);
        Assert.Equal("SILVER", updated!.LoyaltyProgram);
        Assert.Equal(created, updated.Created);

        Assert.True(await repo.DeleteAsync(ada.Id, Guid.Empty));
        Assert.Null(await repo.GetByIdAsync(ada.Id));
    }
}
