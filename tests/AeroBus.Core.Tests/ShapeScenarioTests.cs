using System.Text.Json.Nodes;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Shape Test Lab scenarios: saved requests per shape, seeded from the
/// shape's sample on first visit.
/// </summary>
[Collection("documentforge")]
public class ShapeScenarioTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Scenarios_seed_from_the_shape_sample_and_round_trip()
    {
        var svc = new AeroBus.Core.Services.Rules.RuleAuthoringService(
            fx.Client, new ShapesTestsStubs.NoRuleForge(), new ShapesTestsStubs.NoEvents());

        // First list seeds the starter scenario from the shape's sample.
        var scenarios = await svc.ListScenariosAsync("policy-input");
        Assert.Contains(scenarios, s => s.GetProperty("name").GetString() == "Shape sample");
        Assert.True(scenarios[0].GetProperty("request").TryGetProperty("mode", out _));

        // Add + edit + delete a custom scenario.
        var id = $"scenario-test-{Guid.NewGuid():N}";
        var doc = JsonNode.Parse($$"""
            {
              "id": "{{id}}",
              "shapeId": "policy-input",
              "name": "Two golds",
              "request": { "mode": "included", "customers": [ { "loyaltyTier": "G" }, { "loyaltyTier": "G" } ] }
            }
            """)!;
        await svc.UpsertScenarioAsync(id, doc);

        var listed = await svc.ListScenariosAsync("policy-input");
        Assert.Contains(listed, s => s.GetProperty("id").GetString() == id);

        doc["name"] = "Two golds (renamed)";
        await svc.UpsertScenarioAsync(id, doc);
        listed = await svc.ListScenariosAsync("policy-input");
        Assert.Contains(listed, s => s.GetProperty("name").GetString() == "Two golds (renamed)");

        // Guards
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpsertScenarioAsync("other", doc));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertScenarioAsync(id, JsonNode.Parse($$"""{ "id": "{{id}}", "shapeId": "policy-input" }""")!));

        Assert.True(await svc.DeleteScenarioAsync(id));
    }
}
