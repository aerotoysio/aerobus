using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class StoreBehaviorTests(DocumentForgeFixture fx)
{
    // Regression: DocumentForge creates collections lazily on first insert, so a
    // read against a collection that doesn't exist yet returns 400 "not found".
    // That is an EMPTY result, not an error — otherwise startup preloads of
    // un-seeded collections (e.g. companyconfigs) would crash.
    [Fact]
    public async Task Reads_against_a_nonexistent_collection_are_empty_not_errors()
    {
        var missing = "nope_" + Guid.NewGuid().ToString("N");

        Assert.Empty(await fx.Store.QueryAsync<Continent>(missing));
        Assert.Null(await fx.Store.GetByIdAsync<Continent>(missing, Guid.NewGuid()));
        Assert.Equal(0, await fx.Store.CountAsync(missing));
    }

    // Regression: a full-document update must not drop audit fields the caller
    // omitted. Editing a continent in the admin was wiping its Created date.
    [Fact]
    public async Task Update_preserves_Created_and_refreshes_Updated()
    {
        var continents = new Continents(fx.Store);
        var id = Guid.NewGuid();
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await continents.SaveAsync(new Continent
        {
            Id = id, CompanyId = DocumentForgeFixture.NewCompany(),
            Name = "Oceania", Code = "OC", Status = "Active", Created = created
        });

        // Simulate the admin edit re-saving WITHOUT Created (the bug repro).
        await continents.SaveAsync(new Continent
        {
            Id = id, Name = "Oceania (edited)", Code = "OC", Status = "Active", Created = null
        });

        var got = await continents.GetByIdAsync(id);
        Assert.Equal("Oceania (edited)", got!.Name);
        Assert.NotNull(got.Created);              // not dropped
        Assert.Equal(2020, got.Created!.Value.Year);
        Assert.NotNull(got.Updated);             // stamped on update

        await continents.DeleteAsync(id, Guid.Empty);
    }

    // New in AeroBus: server-side COUNT(*) must agree with the number of
    // documents actually inserted (the old implementation pulled every
    // document back just to count them).
    [Fact]
    public async Task Count_uses_a_server_side_aggregate_and_matches_inserts()
    {
        var continents = new Continents(fx.Store);
        var companyId = DocumentForgeFixture.NewCompany();
        var ids = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            await continents.SaveAsync(new Continent
            {
                Id = id, CompanyId = companyId, Name = $"Continent {i}", Code = $"C{i}", Status = "Active"
            });
        }

        var count = await fx.Store.CountAsync("continents",
            new Dictionary<string, object?> { ["CompanyId"] = companyId });
        Assert.Equal(3, count);

        foreach (var id in ids) await continents.DeleteAsync(id, Guid.Empty);
    }

    // Paging is pushed into SQL (LIMIT/OFFSET, server-side skip-then-take).
    // Page p of size s must be exactly the slice the old client-side
    // Skip((p-1)*s).Take(s) produced over the same unpaged result.
    [Fact]
    public async Task Paged_query_returns_the_same_slice_as_the_unpaged_result()
    {
        var continents = new Continents(fx.Store);
        var companyId = DocumentForgeFixture.NewCompany();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            await continents.SaveAsync(new Continent
            {
                Id = id, CompanyId = companyId, Name = $"Continent {i}", Code = $"P{i}", Status = "Active"
            });
        }

        var filter = new Dictionary<string, object?> { ["CompanyId"] = companyId };
        var all = await fx.Store.QueryAsync<Continent>("continents", filter);
        Assert.Equal(5, all.Count);

        var page2 = await fx.Store.QueryAsync<Continent>("continents", filter, page: 2, size: 2);
        Assert.Equal(all.Skip(2).Take(2).Select(c => c.Id), page2.Select(c => c.Id));

        var headOnly = await fx.Store.QueryAsync<Continent>("continents", filter, size: 3);
        Assert.Equal(all.Take(3).Select(c => c.Id), headOnly.Select(c => c.Id));

        Assert.Empty(await fx.Store.QueryAsync<Continent>("continents", filter, page: 4, size: 2));

        foreach (var id in ids) await continents.DeleteAsync(id, Guid.Empty);
    }

    // New in AeroBus: the conditional-update primitive (atomic compare-and-set,
    // the inventory building block). Guard holds -> mutation applies; guard
    // fails -> 409 with the failed condition and no write.
    [Fact]
    public async Task Conditional_update_applies_when_guard_holds_and_409s_when_it_fails()
    {
        var collection = "cas_" + Guid.NewGuid().ToString("N");

        // Insert directly and query back to learn DocumentForge's internal _id.
        await fx.Client.InsertAsync(collection, """{ "Name": "counter", "Available": 2, "Sold": 0 }""");
        var rows = await fx.Client.QueryAsync($"SELECT * FROM {collection}");
        var row = Assert.Single(rows);
        var dfId = row.GetProperty("_id").GetString()!;

        // Sell one seat: guard Available >= 1 holds.
        var ok = await fx.Client.ConditionalUpdateAsync(collection, dfId,
            [new("Available", ">=", 1)],
            [new("Available", "dec", 1), new("Sold", "inc", 1)]);
        Assert.True(ok.Success);

        // Sell two more: guard Available >= 2 fails (only 1 left) — no write.
        var conflict = await fx.Client.ConditionalUpdateAsync(collection, dfId,
            [new("Available", ">=", 2)],
            [new("Available", "dec", 2), new("Sold", "inc", 2)]);
        Assert.False(conflict.Success);
        Assert.Equal(409, conflict.StatusCode);

        var after = Assert.Single(await fx.Client.QueryAsync($"SELECT * FROM {collection}"));
        Assert.Equal(1, after.GetProperty("Available").GetInt32());
        Assert.Equal(1, after.GetProperty("Sold").GetInt32());

        await fx.Client.DeleteByFieldAsync(collection, "Name", "counter");
    }
}
