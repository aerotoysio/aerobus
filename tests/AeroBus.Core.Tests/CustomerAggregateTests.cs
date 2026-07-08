using AeroBus.Core.Model.Customer;
using AeroBus.Core.Repositories.Customer;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class CustomerAggregateTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Customer_with_passports_and_cards_round_trips()
    {
        var customers = new Customers(fx.Store);
        var id = Guid.NewGuid();
        var customer = new Customer
        {
            Id = id,
            CompanyId = DocumentForgeFixture.NewCompany(),
            CustomerNumber = "CUST-" + id.ToString("N")[..6],
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            Status = "Active",
            Created = DateTime.UtcNow,
            Passports = new() { new Passport { Id = Guid.NewGuid(), CountryCode = "AU", PassportNumber = "PA999", ExpiryDate = DateTime.UtcNow.AddYears(5), Status = "Active" } },
            StoredCards = new() { new StoredCard { Id = "card_1", Provider = "Visa", LastFour = "4242", Status = "Active" } }
        };

        await customers.SaveAsync(customer);
        var got = await customers.GetByIdAsync(id);

        Assert.NotNull(got);
        Assert.Single(got!.Passports!);
        Assert.Equal("PA999", got.Passports![0].PassportNumber);
        Assert.Single(got.StoredCards!);
        Assert.Equal("4242", got.StoredCards![0].LastFour);

        var byNumber = await customers.GetByNumberAsync(customer.CustomerNumber!);
        Assert.NotNull(byNumber);
        Assert.Equal(id, byNumber!.Id);

        await customers.DeleteAsync(id, Guid.Empty);
        Assert.Null(await customers.GetByIdAsync(id));
    }
}
