using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Services.Admin;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Live DocumentForge round-trip for the SaaS db-per-org mechanics: creating an org
/// database, routing a store to it, the physical isolation between org databases,
/// the reference seed pack, and the control-plane registry. (The Keycloak-dependent
/// onboarding step of provisioning isn't exercised here.)
///
/// Collections are warmed first because the local dfdb build returns a bare
/// "collection not found" (without the <c>collectionNotFound</c> code) for a SELECT
/// against a never-created collection, which the store's upsert existence-check
/// would otherwise surface as an error — the same environment quirk documented for
/// the rest of the live suite; a spec-compliant DocumentForge needs no warming.
/// </summary>
[Collection("documentforge")]
public sealed class ProvisioningRoutingTests
{
    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private readonly DocumentForgeFixture _fx;
    private readonly string _shortName = $"prov{Guid.NewGuid():N}"[..14];

    public ProvisioningRoutingTests(DocumentForgeFixture fx) => _fx = fx;

    private IDocumentForgeClient ClientFor(string database) =>
        new DocumentForgeClient(new HttpClient(), new Opt<DocumentForgeOptions>(
            new DocumentForgeOptions { BaseUrl = _fx.BaseUrl, ApiKey = "", Database = database }));

    private IDocumentStore StoreFor(string database) => new DocumentStore(ClientFor(database));

    private static async Task WarmAsync(IDocumentForgeClient client, params string[] collections)
    {
        foreach (var c in collections)
        {
            await client.InsertAsync(c, "{\"id\":\"__warm__\"}");
            await client.DeleteByFieldAsync(c, "id", "__warm__");
        }
    }

    [Fact]
    public async Task EnsureDatabase_creates_a_database_and_is_idempotent()
    {
        Assert.True(await _fx.Client.EnsureDatabaseAsync(_shortName));
        Assert.True(await _fx.Client.EnsureDatabaseAsync(_shortName)); // already exists = success
    }

    [Fact]
    public async Task Tenant_data_is_physically_isolated_to_its_own_database()
    {
        await _fx.Client.EnsureDatabaseAsync(_shortName);
        await WarmAsync(ClientFor(_shortName), DfCollections.Admin.Companies);
        var tenant = StoreFor(_shortName);
        var companyId = Guid.NewGuid();

        await tenant.UpsertAsync(DfCollections.Admin.Companies, new Company
        {
            Id = companyId, Name = "Test Air", Slug = _shortName, Designator = "TA",
            OperatingCurrency = "AED", Status = "Active", Created = DateTime.UtcNow,
        }, companyId);

        // Present in the org's own database…
        Assert.NotNull(await tenant.GetByIdAsync<Company>(DfCollections.Admin.Companies, companyId));
        // …and NOT visible in the default/control database (true separation).
        Assert.Null(await _fx.Store.GetByIdAsync<Company>(DfCollections.Admin.Companies, companyId));
    }

    [Fact]
    public async Task Reference_seed_pack_lands_in_the_org_database()
    {
        await _fx.Client.EnsureDatabaseAsync(_shortName);
        await WarmAsync(ClientFor(_shortName), DfCollections.Catalogue.Airports, DfCollections.Catalogue.Equipment);
        var tenant = StoreFor(_shortName);
        var companyId = Guid.NewGuid();

        await ReferenceSeed.SeedAsync(tenant, companyId);

        var airports = await tenant.QueryAsync<Airport>(DfCollections.Catalogue.Airports);
        Assert.True(airports.Count >= 5);
        Assert.Contains(airports, a => a.Code == "DXB");
        Assert.All(airports, a => Assert.Equal(companyId, a.CompanyId));
    }

    [Fact]
    public async Task Organisation_registry_round_trips_in_the_control_database()
    {
        await WarmAsync(_fx.Client, DfCollections.Admin.Organisations);
        var orgs = new Organisations(_fx.Store);
        var companyId = Guid.NewGuid();

        await orgs.SaveAsync(new Organisation
        {
            Id = companyId, OrgAlias = _shortName, ShortName = _shortName, Name = "Test Air",
            Designator = "TA", OperatingCurrency = "AED", Status = "Active", Created = DateTime.UtcNow,
        });

        Assert.Equal(_shortName, (await orgs.GetByIdAsync(companyId))?.ShortName);
        Assert.Equal(companyId, (await orgs.GetByShortNameAsync(_shortName))?.Id);
    }
}
