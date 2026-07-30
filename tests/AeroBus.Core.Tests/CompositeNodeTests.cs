using System.Text.Json;
using System.Text.Json.Nodes;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Specialised nodes: ONE Market filter node compiles into the primitive
/// filter chain — (o∈US ∧ d∈DXB) ∨ (o∈DXB ∧ d∈US) when bidirectional — and
/// the engine evaluates the expanded graph correctly.
/// </summary>
[Collection("documentforge")]
public class CompositeNodeTests(DocumentForgeFixture fx)
{
    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private sealed class StaticSettingsScope : IRuleForgeSettingsProvider
    {
        public Task<RuleForgeSettings> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new RuleForgeSettings("embedded", "", 2000, "dev"));
    }

    private static readonly Dictionary<string, string[]> Zones = new()
    {
        ["us-zone"] = ["JFK", "MIA", "LAX", "LAS"],
        ["dxb-zone"] = ["DXB"],
    };

    private static Task<JsonElement?> GetTemplate(string id) =>
        Task.FromResult<JsonElement?>(id == "market-filter"
            ? JsonDocument.Parse(NodeTemplateDefaults.MarketFilterJson).RootElement
            : null);

    private static Task<IReadOnlyList<string>> ResolveZone(string id) =>
        Task.FromResult<IReadOnlyList<string>>(Zones[id]);

    private static JsonObject MarketRule(string direction) => JsonNode.Parse($$"""
        {
          "id": "rule-us-dxb",
          "name": "US-Dubai market rule",
          "endpoint": "/v1/test/us-dxb",
          "method": "POST",
          "status": "draft",
          "currentVersion": 0,
          "inputSchema": {},
          "outputSchema": {},
          "updatedAt": "2026-07-29T00:00:00Z",
          "nodes": [
            { "id": "n-in", "type": "input", "position": {"x":0,"y":0}, "data": { "label": "Request", "category": "input", "config": {} } },
            { "id": "n-market", "type": "composite", "position": {"x":200,"y":0}, "data": { "label": "US <-> Dubai", "category": "filter",
              "config": { "templateId": "market-filter", "params": { "from": "us-zone", "to": "dxb-zone", "direction": "{{direction}}" } } } },
            { "id": "n-grant", "type": "product", "position": {"x":400,"y":0}, "data": { "label": "Grant", "category": "product",
              "config": { "output": { "code": "LOUNGE", "name": "Lounge access" } } } },
            { "id": "n-out", "type": "output", "position": {"x":600,"y":0}, "data": { "label": "Out", "category": "output", "config": {} } }
          ],
          "edges": [
            { "id": "e1", "source": "n-in", "target": "n-market", "branch": "default" },
            { "id": "e2", "source": "n-market", "target": "n-grant", "branch": "pass" },
            { "id": "e3", "source": "n-grant", "target": "n-out", "branch": "default" }
          ]
        }
        """)!.AsObject();

    [Fact]
    public async Task One_way_expands_to_two_filters_with_zone_airports()
    {
        var expanded = await CompositeNodeExpander.ExpandAsync(MarketRule("one-way"), GetTemplate, ResolveZone);

        var nodes = expanded["nodes"]!.AsArray();
        var filters = nodes.Where(n => n!["type"]!.GetValue<string>() == "stringFilter").ToList();
        Assert.Equal(2, filters.Count);
        Assert.DoesNotContain(nodes, n => n!["type"]!.GetValue<string>() == "composite");

        var origin = filters.Single(f => f!["id"]!.GetValue<string>() == "n-market__out");
        var values = origin!["data"]!["config"]!["compare"]!["values"]!.AsArray().Select(v => v!.GetValue<string>());
        Assert.Equal(["JFK", "MIA", "LAX", "LAS"], values);

        // incoming edge retargeted to the entry; $pass exit reaches the product
        var edges = expanded["edges"]!.AsArray();
        Assert.Contains(edges, e => e!["source"]!.GetValue<string>() == "n-in" && e["target"]!.GetValue<string>() == "n-market__out");
        Assert.Contains(edges, e => e!["source"]!.GetValue<string>() == "n-market__outDest" && e["target"]!.GetValue<string>() == "n-grant" && e["branch"]!.GetValue<string>() == "pass");
    }

    [Fact]
    public async Task Bidirectional_expands_with_the_return_branch_on_fail()
    {
        var expanded = await CompositeNodeExpander.ExpandAsync(MarketRule("bidirectional"), GetTemplate, ResolveZone);

        var nodes = expanded["nodes"]!.AsArray();
        Assert.Equal(4, nodes.Count(n => n!["type"]!.GetValue<string>() == "stringFilter"));
        var edges = expanded["edges"]!.AsArray();
        Assert.Contains(edges, e => e!["source"]!.GetValue<string>() == "n-market__out"
            && e["target"]!.GetValue<string>() == "n-market__back" && e["branch"]!.GetValue<string>() == "fail");
    }

    [Fact]
    public async Task Engine_evaluates_the_expanded_market_rule_in_both_directions()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRuleForgeSettingsProvider, StaticSettingsScope>();
        var provider = services.BuildServiceProvider();
        await using var embedded = new EmbeddedRuleForgeClient(
            new Opt<AeroBus.Core.Data.DocumentForgeOptions>(new AeroBus.Core.Data.DocumentForgeOptions
            {
                BaseUrl = fx.BaseUrl,
                ApiKey = Environment.GetEnvironmentVariable("DOCUMENTFORGE_APIKEY") ?? "",
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmbeddedRuleForgeClient>.Instance);

        var expanded = await CompositeNodeExpander.ExpandAsync(MarketRule("bidirectional"), GetTemplate, ResolveZone);
        var ruleJson = JsonSerializer.Deserialize<JsonElement>(expanded.ToJsonString());

        async Task<Decision> Shop(string origin, string destination)
        {
            var request = JsonSerializer.SerializeToElement(new
            {
                mode = "included",
                flightSolution = new { origin, destination },
            });
            return (await embedded.EvaluateDraftAsync(ruleJson, request)).Decision;
        }

        Assert.Equal(Decision.Apply, await Shop("JFK", "DXB"));  // outbound
        Assert.Equal(Decision.Apply, await Shop("DXB", "MIA"));  // return direction
        Assert.Equal(Decision.Skip, await Shop("LHR", "CDG"));   // outside both markets
        Assert.Equal(Decision.Skip, await Shop("JFK", "LHR"));   // right origin, wrong destination

        // one-way: the return direction no longer applies
        var oneWay = await CompositeNodeExpander.ExpandAsync(MarketRule("one-way"), GetTemplate, ResolveZone);
        var oneWayJson = JsonSerializer.Deserialize<JsonElement>(oneWay.ToJsonString());
        var request2 = JsonSerializer.SerializeToElement(new { mode = "included", flightSolution = new { origin = "DXB", destination = "MIA" } });
        Assert.Equal(Decision.Skip, (await embedded.EvaluateDraftAsync(oneWayJson, request2)).Decision);
    }
}
