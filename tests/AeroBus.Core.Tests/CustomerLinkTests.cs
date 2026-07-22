using AeroBus.Core.Model.Customer;
using AeroBus.Core.Repositories.Customer;
using AeroBus.Core.Services.Customer;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// The single-identity pattern: passengers with contact data create-or-link a
/// customer (email first, then phone + surname, both case-insensitive); the
/// copied instance data stays on the passenger; unidentifiable passengers
/// (children without contact) link to nothing.
/// </summary>
[Collection("documentforge")]
public class CustomerLinkTests(DocumentForgeFixture fx)
{
    private sealed class NullEvents : Core.Events.IEventPublisher
    {
        public Task<Core.Events.OutboxEvent?> PublishAsync(
            string type, Core.Events.EventSubject subject, object data, Guid? companyId, string? actor = null, CancellationToken ct = default) =>
            Task.FromResult<Core.Events.OutboxEvent?>(null);
    }

    [Fact]
    public async Task Creates_then_links_by_email_case_insensitively_and_skips_contactless_pax()
    {
        var company = DocumentForgeFixture.NewCompany();
        var customers = new Customers(fx.Store);
        var linker = new CustomerLinker(customers, new NullEvents(), NullLogger<CustomerLinker>.Instance);

        var adult = new Passenger
        {
            PaxType = "ADT", FirstName = "Webby", LastName = "Traveller",
            Email = "Webby.Traveller@Example.com", Phone = "+971 50 123 4567",
        };
        var child = new Passenger { PaxType = "CHD", FirstName = "Mini", LastName = "Traveller" };

        // First order: customer is CREATED, lead profile returned, child unlinked.
        var lead = await linker.LinkAsync(company, new[] { adult, child });
        Assert.NotNull(lead);
        Assert.Equal(lead, adult.CustomerId);
        Assert.Null(child.CustomerId);

        var stored = await customers.FindByEmailAsync(company, "webby.traveller@example.com");
        Assert.NotNull(stored);
        Assert.Equal(lead, stored!.Id);
        Assert.Equal("webby.traveller@example.com", stored.Email); // normalized at rest
        Assert.StartsWith("CU", stored.CustomerNumber);

        // Second order, different casing: LINKS to the same identity, creates nothing new.
        var again = new Passenger
        {
            PaxType = "ADT", FirstName = "Webby", LastName = "Traveller",
            Email = "WEBBY.TRAVELLER@EXAMPLE.COM",
        };
        var lead2 = await linker.LinkAsync(company, new[] { again });
        Assert.Equal(lead, lead2);
        Assert.Equal(lead, again.CustomerId);
    }

    [Fact]
    public async Task Falls_back_to_phone_plus_surname_when_no_email()
    {
        var company = DocumentForgeFixture.NewCompany();
        var customers = new Customers(fx.Store);
        var linker = new CustomerLinker(customers, new NullEvents(), NullLogger<CustomerLinker>.Instance);

        var first = new Passenger
        {
            PaxType = "ADT", FirstName = "Pat", LastName = "Caller", Phone = "+61 400 111 222",
        };
        var lead = await linker.LinkAsync(company, new[] { first });
        Assert.NotNull(lead);

        // Same phone + surname (different spacing/case) → same customer.
        var second = new Passenger
        {
            PaxType = "ADT", FirstName = "Patrick", LastName = "CALLER", Phone = "+61400111222",
        };
        var lead2 = await linker.LinkAsync(company, new[] { second });
        Assert.Equal(lead, lead2);

        // Same phone, DIFFERENT surname → a distinct identity.
        var other = new Passenger
        {
            PaxType = "ADT", FirstName = "Sam", LastName = "Stranger", Phone = "+61 400 111 222",
        };
        var lead3 = await linker.LinkAsync(company, new[] { other });
        Assert.NotNull(lead3);
        Assert.NotEqual(lead, lead3);
    }
}
