using System.Text.Json;
using System.Text.Json.Nodes;

namespace AeroBus.Core.Services.Rules
{
    /// <summary>
    /// Expands COMPOSITE nodes (specialised business nodes like the Market
    /// filter) into the primitive subgraph their node template declares. The
    /// author sees ONE friendly node; publish and the test console compile it
    /// down before the engine ever sees it — the engine stays pure primitives.
    ///
    /// A composite node is <c>type: "composite"</c> with
    /// <c>config: { templateId, params: {...} }</c>. The template's
    /// <c>expansion</c> is a parameterised subgraph:
    /// <list type="bullet">
    /// <item><c>${param:key}</c> in any config string substitutes the param value;</item>
    /// <item><c>${zone:key}</c> substitutes the Market Zone's built airport list
    /// (the param holds a zone id) — "from the US" stays a country selection,
    /// not a hand-typed airport list;</item>
    /// <item>nodes/edges may carry <c>when: "key=value"</c> to include them only
    /// for some param values (e.g. the return-direction branch when
    /// direction=bidirectional);</item>
    /// <item>edges to <c>$pass</c> exit the composite along its original
    /// pass/default successors.</item>
    /// </list>
    /// </summary>
    public static class CompositeNodeExpander
    {
        public static bool HasComposites(JsonObject rule) =>
            rule["nodes"] is JsonArray nodes &&
            nodes.Any(n => n?["type"]?.GetValue<string>() == "composite");

        public static async Task<JsonObject> ExpandAsync(
            JsonObject rule,
            Func<string, Task<JsonElement?>> getTemplate,
            Func<string, Task<IReadOnlyList<string>>> resolveZoneAirports,
            CancellationToken ct = default)
        {
            if (rule["nodes"] is not JsonArray nodes || rule["edges"] is not JsonArray edges)
                return rule;

            var outNodes = new JsonArray();
            var outEdges = new List<JsonObject>();
            var retargets = new Dictionary<string, string>(StringComparer.Ordinal);   // composite id -> entry id
            var exitEdges = new List<(string Source, string CompositeId, string Branch)>();

            foreach (var n in nodes.OfType<JsonObject>())
            {
                if (n["type"]?.GetValue<string>() != "composite")
                {
                    outNodes.Add(n.DeepClone());
                    continue;
                }

                var compositeId = n["id"]!.GetValue<string>();
                var config = n["data"]?["config"] as JsonObject
                             ?? throw new InvalidOperationException($"Composite node '{compositeId}' has no config.");
                var templateId = config["templateId"]?.GetValue<string>()
                                 ?? throw new InvalidOperationException($"Composite node '{compositeId}' names no templateId.");
                var paramValues = (config["params"] as JsonObject) ?? new JsonObject();

                var template = await getTemplate(templateId)
                               ?? throw new InvalidOperationException($"Node template '{templateId}' was not found.");
                if (!template.TryGetProperty("expansion", out var expansion))
                    throw new InvalidOperationException($"Node template '{templateId}' declares no expansion.");

                var baseX = n["position"]?["x"]?.GetValue<double>() ?? 0;
                var baseY = n["position"]?["y"]?.GetValue<double>() ?? 0;

                string Qualified(string localId) => $"{compositeId}__{localId}";
                bool Included(JsonElement el) =>
                    !el.TryGetProperty("when", out var w) || WhenMatches(w.GetString(), paramValues);

                // Nodes: substitute params/zones, offset positions below the composite.
                var index = 0;
                foreach (var tn in expansion.GetProperty("nodes").EnumerateArray())
                {
                    if (!Included(tn)) continue;
                    var node = JsonNode.Parse(tn.GetRawText())!.AsObject();
                    node.Remove("when");
                    var localId = node["id"]!.GetValue<string>();
                    node["id"] = Qualified(localId);
                    node["position"] = new JsonObject { ["x"] = baseX, ["y"] = baseY + index * 110 };
                    var label = node["label"]?.GetValue<string>() ?? localId;
                    node.Remove("label");
                    var cfg = node["config"] as JsonObject ?? new JsonObject();
                    node.Remove("config");
                    node["data"] = new JsonObject
                    {
                        ["label"] = $"{n["data"]?["label"]?.GetValue<string>() ?? templateId}: {label}",
                        ["category"] = CategoryFor(node["type"]?.GetValue<string>()),
                        ["templateId"] = templateId,
                        ["config"] = await SubstituteAsync(cfg, paramValues, resolveZoneAirports),
                    };
                    outNodes.Add(node);
                    index++;
                }

                var entry = Qualified(expansion.GetProperty("entry").GetString()!);
                retargets[compositeId] = entry;

                foreach (var te in expansion.GetProperty("edges").EnumerateArray())
                {
                    if (!Included(te)) continue;
                    var from = Qualified(te.GetProperty("from").GetString()!);
                    var to = te.GetProperty("to").GetString()!;
                    var branch = te.TryGetProperty("branch", out var b) ? b.GetString()! : "default";
                    if (to == "$pass")
                        exitEdges.Add((from, compositeId, branch));
                    else
                        outEdges.Add(Edge($"{compositeId}__e{outEdges.Count}", from, Qualified(to), branch));
                }
            }

            // Original edges: retarget INTO composites to their entry; edges OUT of
            // composites are replaced by the subgraph's $pass exits.
            foreach (var e in edges.OfType<JsonObject>())
            {
                var source = e["source"]!.GetValue<string>();
                var target = e["target"]!.GetValue<string>();
                var branch = e["branch"]?.GetValue<string>() ?? "default";

                if (retargets.ContainsKey(source))
                {
                    // pass/default successors of the composite receive the exits.
                    if (branch is "pass" or "default")
                        foreach (var (exitSource, _, exitBranch) in exitEdges.Where(x => x.CompositeId == source))
                            outEdges.Add(Edge($"{e["id"]!.GetValue<string>()}__{exitSource}", exitSource, RetargetIfComposite(target), exitBranch));
                    continue; // fail successors of a composite drop: engine treats dead branch as skip
                }

                outEdges.Add(Edge(e["id"]!.GetValue<string>(), source, RetargetIfComposite(target), branch));
            }

            string RetargetIfComposite(string id) => retargets.TryGetValue(id, out var entry) ? entry : id;

            var result = JsonNode.Parse(rule.ToJsonString())!.AsObject();
            result["nodes"] = outNodes;
            var edgeArray = new JsonArray();
            foreach (var e in outEdges) edgeArray.Add(e);
            result["edges"] = edgeArray;
            return result;
        }

        private static JsonObject Edge(string id, string source, string target, string branch) => new()
        {
            ["id"] = id,
            ["source"] = source,
            ["target"] = target,
            ["branch"] = branch,
        };

        private static bool WhenMatches(string? when, JsonObject paramValues)
        {
            if (string.IsNullOrWhiteSpace(when)) return true;
            var parts = when.Split('=', 2);
            if (parts.Length != 2) return true;
            return string.Equals(paramValues[parts[0].Trim()]?.GetValue<string>(), parts[1].Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string CategoryFor(string? type) => type switch
        {
            "stringFilter" or "numberFilter" or "dateFilter" => "filter",
            "product" => "product",
            "constant" => "constant",
            _ => type ?? "logic",
        };

        /// <summary>Depth-first substitution of ${param:key} and ${zone:key} placeholders.</summary>
        private static async Task<JsonNode?> SubstituteAsync(
            JsonNode? node, JsonObject paramValues, Func<string, Task<IReadOnlyList<string>>> resolveZoneAirports)
        {
            switch (node)
            {
                case JsonObject obj:
                {
                    var result = new JsonObject();
                    foreach (var (key, value) in obj)
                        result[key] = await SubstituteAsync(value, paramValues, resolveZoneAirports);
                    return result;
                }
                case JsonArray arr:
                {
                    var result = new JsonArray();
                    foreach (var item in arr)
                        result.Add(await SubstituteAsync(item, paramValues, resolveZoneAirports));
                    return result;
                }
                case JsonValue val when val.TryGetValue<string>(out var s):
                {
                    if (s.StartsWith("${zone:", StringComparison.Ordinal) && s.EndsWith('}'))
                    {
                        var key = s["${zone:".Length..^1];
                        var zoneId = paramValues[key]?.GetValue<string>()
                                     ?? throw new InvalidOperationException($"Composite param '{key}' (a market zone) is not set.");
                        var airports = await resolveZoneAirports(zoneId);
                        var arr = new JsonArray();
                        foreach (var a in airports) arr.Add(a);
                        return arr;
                    }
                    if (s.StartsWith("${param:", StringComparison.Ordinal) && s.EndsWith('}'))
                        return paramValues[s["${param:".Length..^1]]?.DeepClone();
                    return val.DeepClone();
                }
                default:
                    return node?.DeepClone();
            }
        }
    }
}
