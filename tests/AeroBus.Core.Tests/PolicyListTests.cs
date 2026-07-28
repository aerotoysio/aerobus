using System.Text.Json.Nodes;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Policies are plain rules with category "policy" — the list endpoint's
/// category filter is what makes them their own vertical in Studio.
/// </summary>
[Collection("documentforge")]
public class PolicyListTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Rules_list_filters_by_category()
    {
        var svc = new AeroBus.Core.Services.Rules.RuleAuthoringService(
            fx.Client, new ShapesTestsStubs.NoRuleForge(), new ShapesTestsStubs.NoEvents());

        var policyId = $"policy-test-{Guid.NewGuid():N}";
        var otherId = $"rule-test-{Guid.NewGuid():N}";

        foreach (var (id, category) in new[] { (policyId, "policy"), (otherId, "pricing") })
            await svc.UpsertRuleAsync(id, JsonNode.Parse($$"""
                {
                  "id": "{{id}}",
                  "name": "Test {{category}}",
                  "category": "{{category}}",
                  "endpoint": "/v1/test/{{id}}",
                  "method": "POST",
                  "nodes": [], "edges": []
                }
                """)!);

        var policies = await svc.ListRulesAsync(null, "policy");
        Assert.Contains(policies, r => r.GetProperty("id").GetString() == policyId);
        Assert.DoesNotContain(policies, r => r.GetProperty("id").GetString() == otherId);

        // status + category compose
        var draftPolicies = await svc.ListRulesAsync("draft", "policy");
        Assert.Contains(draftPolicies, r => r.GetProperty("id").GetString() == policyId);

        await svc.DeleteRuleAsync(policyId);
        await svc.DeleteRuleAsync(otherId);
    }
}
