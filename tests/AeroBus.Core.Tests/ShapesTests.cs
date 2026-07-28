using System.Text.Json.Nodes;
using AeroBus.Core.Events;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Rules;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Input shapes (docs/rule-based-retailing.md): stored documents in the flat
/// <c>shapes</c> collection, lazily seeded with the three defaults, editable
/// through the authoring service.
/// </summary>
/// <summary>Inert engine/event stubs shared by authoring-service tests.</summary>
public static class ShapesTestsStubs
{
    public sealed class NoRuleForge : IRuleForgeClient
    {
        public Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<bool> HealthAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RefreshAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    public sealed class NoEvents : IEventPublisher
    {
        public Task<OutboxEvent?> PublishAsync(string type, EventSubject subject, object data, Guid? companyId, string? actor = null, CancellationToken ct = default) =>
            Task.FromResult<OutboxEvent?>(null);
    }
}

[Collection("documentforge")]
public class ShapesTests(DocumentForgeFixture fx)
{
    private RuleAuthoringService Service() =>
        new(fx.Client, new ShapesTestsStubs.NoRuleForge(), new ShapesTestsStubs.NoEvents());

    [Fact]
    public async Task Shapes_seed_lazily_and_round_trip_edits()
    {
        var svc = Service();

        // First list seeds the three defaults (idempotent for later runs).
        var shapes = await svc.ListShapesAsync();
        Assert.Contains(shapes, s => s.GetProperty("id").GetString() == "shopping");
        Assert.Contains(shapes, s => s.GetProperty("id").GetString() == "rtf-benefits");
        Assert.Contains(shapes, s => s.GetProperty("id").GetString() == "a-la-carte");

        var shopping = (await svc.GetShapeAsync("shopping"))!.Value;
        Assert.Contains(shopping.GetProperty("fields").EnumerateArray(),
            f => f.GetProperty("path").GetString() == "customers.loyaltyTier");
        Assert.Equal("included", shopping.GetProperty("sample").GetProperty("mode").GetString());

        // Edit round-trip on a scratch shape (leave the defaults alone).
        var scratchId = $"test-shape-{Guid.NewGuid():N}";
        var doc = JsonNode.Parse($$"""
            {
              "id": "{{scratchId}}",
              "name": "Test shape",
              "fields": [ { "path": "foo.bar", "type": "string", "label": "Foo bar" } ],
              "sample": { "foo": { "bar": "baz" } }
            }
            """)!;
        await svc.UpsertShapeAsync(scratchId, doc);
        Assert.Equal("Test shape", (await svc.GetShapeAsync(scratchId))!.Value.GetProperty("name").GetString());

        doc["name"] = "Renamed shape";
        await svc.UpsertShapeAsync(scratchId, doc);
        Assert.Equal("Renamed shape", (await svc.GetShapeAsync(scratchId))!.Value.GetProperty("name").GetString());

        // Guards: id mismatch and missing fields array.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertShapeAsync("other-id", doc));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertShapeAsync(scratchId, JsonNode.Parse($$"""{ "id": "{{scratchId}}" }""")!));

        Assert.True(await svc.DeleteShapeAsync(scratchId));
        Assert.Null(await svc.GetShapeAsync(scratchId));
    }
}
