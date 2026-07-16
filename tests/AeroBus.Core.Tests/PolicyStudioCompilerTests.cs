using System.Text.Json.Nodes;
using AeroBus.Core.Services.PolicyStudio;

namespace AeroBus.Core.Tests;

/// <summary>
/// Policy Studio compiler tests. AeroBus treats RuleForge as an external HTTP
/// engine and does not reference its Core library, so — unlike the original
/// RuleForge.Admin suite — these assert on the compiled rule-graph <b>structure</b>
/// (nodes/edges/operator mappings/verdict wiring) rather than executing the graph
/// through an in-process RuleRunner. End-to-end engine fidelity is covered by the
/// live-stack verification (publish → /rules → engine refresh).
/// </summary>
public class PolicyStudioCompilerTests
{
    private static JsonObject InfantPolicy() => new()
    {
        ["title"] = "Infant Passenger Policy",
        ["version"] = 4,
        ["statements"] = new JsonArray(
            Statement("s0", "AND",
                Condition("Pax.Type", "enum", "=", "INF"),
                Condition("Pax.DOB", "date", "<", "2", fn: "age", unit: "years")),
            Statement("s1", "AND",
                Condition("Pax.Type", "enum", "=", "INF"),
                Condition("Pax.AccompaniedBy", "enum", "=", "ADT")),
            Statement("s2", "AND",
                Condition("Booking.InfantCount", "number", "<=", "2"),
                Condition("Booking.AdultCount", "number", ">=", "1"))),
    };

    private static JsonObject Statement(string id, string joiner, params JsonObject[] conditions) => new()
    {
        ["id"] = id, ["text"] = id, ["joiner"] = joiner,
        ["conditions"] = new JsonArray(conditions.Cast<JsonNode>().ToArray()),
    };

    private static JsonObject Condition(string fieldId, string type, string op, string value, string? fn = null, string? unit = null)
    {
        var c = new JsonObject
        {
            ["field"] = new JsonObject { ["id"] = fieldId, ["type"] = type },
            ["op"] = op, ["value"] = value,
        };
        if (fn is not null) c["fn"] = fn;
        if (unit is not null) c["unit"] = unit;
        return c;
    }

    // ── graph structure helpers ──────────────────────────────────────────────

    private static JsonArray Nodes(JsonObject rule) => rule["nodes"]!.AsArray();
    private static JsonArray Edges(JsonObject rule) => rule["edges"]!.AsArray();

    private static IEnumerable<JsonObject> NodesOfType(JsonObject rule, string type) =>
        Nodes(rule).OfType<JsonObject>().Where(n => n["type"]?.GetValue<string>() == type);

    private static JsonObject? NodeById(JsonObject rule, string id) =>
        Nodes(rule).OfType<JsonObject>().FirstOrDefault(n => n["id"]?.GetValue<string>() == id);

    private static bool HasEdge(JsonObject rule, string source, string target, string branch) =>
        Edges(rule).OfType<JsonObject>().Any(e =>
            e["source"]?.GetValue<string>() == source &&
            e["target"]?.GetValue<string>() == target &&
            e["branch"]?.GetValue<string>() == branch);

    private static string? FilterOperator(JsonObject node) =>
        node["data"]?["config"]?["compare"]?["operator"]?.GetValue<string>();

    // ── header + skeleton ────────────────────────────────────────────────────

    [Fact]
    public void Compiled_rule_carries_policy_header()
    {
        var rule = RuleCompiler.Compile(InfantPolicy(), "d-infant", "/v1/policies/infant").Rule;
        Assert.Equal("rule-d-infant", rule["id"]!.GetValue<string>());
        Assert.Equal("Infant Passenger Policy", rule["name"]!.GetValue<string>());
        Assert.Equal("/v1/policies/infant", rule["endpoint"]!.GetValue<string>());
        Assert.Equal("POST", rule["method"]!.GetValue<string>());
        Assert.Equal(4, rule["currentVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Compiled_rule_has_input_output_and_two_verdict_constants()
    {
        var rule = RuleCompiler.Compile(InfantPolicy(), "d-infant", "/v1/policies/infant").Rule;
        Assert.Single(NodesOfType(rule, "input"));
        Assert.Single(NodesOfType(rule, "output"));
        Assert.Equal(2, NodesOfType(rule, "constant").Count());
    }

    [Fact]
    public void Verdict_node_routes_pass_to_valid_and_fail_to_reject()
    {
        var rule = RuleCompiler.Compile(InfantPolicy(), "d-infant", "/v1/policies/infant").Rule;
        Assert.True(HasEdge(rule, "n-verdict", "n-valid", "pass"));
        Assert.True(HasEdge(rule, "n-verdict", "n-reject", "fail"));
        Assert.True(HasEdge(rule, "n-valid", "n-output", "default"));
        Assert.True(HasEdge(rule, "n-reject", "n-output", "default"));
    }

    [Fact]
    public void Constant_nodes_carry_the_verdict_payload()
    {
        var rule = RuleCompiler.Compile(InfantPolicy(), "d-infant", "/v1/policies/infant").Rule;
        var valid = NodeById(rule, "n-valid")!["data"]!["config"]!["value"]!;
        Assert.Equal("VALID", valid["verdict"]!.GetValue<string>());
        Assert.Equal("d-infant", valid["policyId"]!.GetValue<string>());
        Assert.Equal(4, valid["policyVersion"]!.GetValue<int>());
    }

    // ── operator / inversion mappings ────────────────────────────────────────

    [Fact]
    public void String_equals_maps_to_case_insensitive_filter()
    {
        var policy = new JsonObject
        {
            ["title"] = "Enum gate", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "AND", Condition("Pax.Type", "enum", "=", "INF"))),
        };
        var rule = RuleCompiler.Compile(policy, "d-enum", "/v1/policies/enum").Rule;
        var filter = NodeById(rule, "s0-c0")!;
        Assert.Equal("sys-filter-str", filter["data"]!["templateId"]!.GetValue<string>());
        Assert.Equal("equals", FilterOperator(filter));
        Assert.True(filter["data"]!["config"]!["compare"]!["caseInsensitive"]!.GetValue<bool>());
    }

    [Fact]
    public void Number_lte_maps_to_lte_operator()
    {
        var policy = new JsonObject
        {
            ["title"] = "Count gate", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "AND", Condition("Booking.InfantCount", "number", "<=", "2"))),
        };
        var rule = RuleCompiler.Compile(policy, "d-num", "/v1/policies/num").Rule;
        Assert.Equal("lte", FilterOperator(NodeById(rule, "s0-c0")!));
    }

    [Fact]
    public void Age_gte_inverts_through_a_not_node()
    {
        var policy = new JsonObject
        {
            ["title"] = "Adults only", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "AND",
                    Condition("Pax.DOB", "date", ">=", "18", fn: "age", unit: "years"))),
        };
        var rule = RuleCompiler.Compile(policy, "d-adult", "/v1/policies/adult").Rule;
        // age >= compiles a within_last date filter, inverted through a NOT logic node.
        var filter = NodeById(rule, "s0-c0")!;
        Assert.Equal("within_last", FilterOperator(filter));
        var not = NodeById(rule, "s0-c0-not")!;
        Assert.Equal("logic", not["type"]!.GetValue<string>());
        Assert.True(HasEdge(rule, "s0-c0", "s0-c0-not", "default"));
    }

    [Fact]
    public void Date_lte_compiles_as_not_after()
    {
        var policy = new JsonObject
        {
            ["title"] = "Booking window", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "AND",
                    Condition("Segment.Departure", "date", "<=", "2026-08-01"))),
        };
        var rule = RuleCompiler.Compile(policy, "d-date", "/v1/policies/date").Rule;
        Assert.Equal("after", FilterOperator(NodeById(rule, "s0-c0")!));
        Assert.NotNull(NodeById(rule, "s0-c0-not"));
    }

    [Fact]
    public void Or_joiner_builds_an_or_logic_node_over_the_terminals()
    {
        var policy = new JsonObject
        {
            ["title"] = "Channel gate", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "OR",
                    Condition("Booking.Channel", "enum", "=", "WEB"),
                    Condition("Booking.Channel", "enum", "=", "MOBILE"))),
        };
        var rule = RuleCompiler.Compile(policy, "d-or", "/v1/policies/or").Rule;
        var logic = NodeById(rule, "s0-or")!;
        Assert.Equal("logic", logic["type"]!.GetValue<string>());
        Assert.True(HasEdge(rule, "s0-c0", "s0-or", "default"));
        Assert.True(HasEdge(rule, "s0-c1", "s0-or", "default"));
    }

    [Fact]
    public void Boolean_field_emits_a_warning_but_still_compiles()
    {
        var policy = new JsonObject
        {
            ["title"] = "Corp gate", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "AND", Condition("Booking.Corporate", "boolean", "=", "true"))),
        };
        var result = RuleCompiler.Compile(policy, "d-bool", "/v1/policies/bool");
        Assert.NotEmpty(result.Warnings);
        Assert.NotNull(NodeById(result.Rule, "s0-c0"));
    }

    // ── compile-time failures (ported unchanged) ─────────────────────────────

    [Fact]
    public void Empty_policy_fails_compilation()
    {
        var policy = new JsonObject
        {
            ["title"] = "Empty", ["version"] = 1, ["statements"] = new JsonArray(),
        };
        var ex = Assert.Throws<RuleCompiler.CompileException>(
            () => RuleCompiler.Compile(policy, "d-empty", "/v1/policies/empty"));
        Assert.Contains("no compilable rule statements", ex.Errors[0]);
    }

    [Fact]
    public void Unsupported_string_operator_fails_compilation()
    {
        var policy = new JsonObject
        {
            ["title"] = "Bad op", ["version"] = 1,
            ["statements"] = new JsonArray(
                Statement("s0", "AND", Condition("Pax.Type", "enum", "<", "INF"))),
        };
        var ex = Assert.Throws<RuleCompiler.CompileException>(
            () => RuleCompiler.Compile(policy, "d-bad", "/v1/policies/bad"));
        Assert.Contains("not supported for string/enum", ex.Errors[0]);
    }
}
