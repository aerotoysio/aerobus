using System.Text.Json.Nodes;
using AeroBus.Core.Repositories.PolicyStudio;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Live DocumentForge round-trip for <see cref="PolicyStudioStore"/> — the seam
/// rewritten from RuleForge.Admin's DfClient onto AeroBus's IDocumentForgeClient.
/// Verifies the CRUD/query surface AND that the dotted <c>policystudio.</c>
/// collection prefix round-trips through DF (SELECT + by-field routes). Each run
/// tags its docs with a unique marker and deletes them, so it is repeatable.
/// </summary>
[Collection("documentforge")]
public sealed class PolicyStudioStoreTests
{
    private readonly PolicyStudioStore _store;
    private readonly string _marker = $"run-{Guid.NewGuid():N}";

    public PolicyStudioStoreTests(DocumentForgeFixture fx) => _store = new PolicyStudioStore(fx.Client);

    private JsonObject Doc(string title) => new() { ["title"] = title, ["testRun"] = _marker };

    [Fact]
    public async Task Insert_assigns_id_and_strips_bookkeeping_fields()
    {
        var saved = await _store.InsertAsync("spaces", Doc("Alpha"), "sp");
        try
        {
            Assert.StartsWith("sp-", saved["id"]!.GetValue<string>());
            Assert.False(saved.ContainsKey("_id"));
            Assert.False(saved.ContainsKey("_etag"));

            var fetched = await _store.GetByIdAsync("spaces", saved["id"]!.GetValue<string>());
            Assert.NotNull(fetched);
            Assert.Equal("Alpha", fetched!["title"]!.GetValue<string>());
            Assert.False(fetched.ContainsKey("_id"));
        }
        finally
        {
            await _store.DeleteWhereAsync("spaces", $"testRun = '{_marker}'");
        }
    }

    [Fact]
    public async Task Replace_updates_the_document_by_domain_id()
    {
        var saved = await _store.InsertAsync("folders", Doc("Before"), "f");
        var id = saved["id"]!.GetValue<string>();
        try
        {
            saved["title"] = "After";
            var replaced = await _store.ReplaceByIdAsync("folders", id, saved);
            Assert.NotNull(replaced);
            Assert.Equal("After", (await _store.GetByIdAsync("folders", id))!["title"]!.GetValue<string>());

            // Replacing a missing id returns null.
            Assert.Null(await _store.ReplaceByIdAsync("folders", "f-does-not-exist", Doc("x")));
        }
        finally
        {
            await _store.DeleteWhereAsync("folders", $"testRun = '{_marker}'");
        }
    }

    [Fact]
    public async Task List_and_count_see_inserted_docs_then_delete_removes_them()
    {
        await _store.InsertAsync("policies", Doc("P1"), "d");
        await _store.InsertAsync("policies", Doc("P2"), "d");

        var mine = await _store.ListAsync("policies", $"testRun = '{_marker}'");
        Assert.Equal(2, mine.Count);
        Assert.All(mine, d => Assert.False(d.ContainsKey("_id")));

        var deleted = await _store.DeleteWhereAsync("policies", $"testRun = '{_marker}'");
        Assert.Equal(2, deleted);
        Assert.Empty(await _store.ListAsync("policies", $"testRun = '{_marker}'"));
    }

    [Fact]
    public async Task Delete_by_id_returns_false_when_absent()
    {
        Assert.False(await _store.DeleteByIdAsync("tests", "t-never-existed"));
    }
}
