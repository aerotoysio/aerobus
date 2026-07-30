namespace AeroBus.Core.Services.Rules
{
    /// <summary>
    /// Seeded specialised-node templates: the business-language nodes that
    /// encapsulate groups of primitives (docs/rule-based-retailing.md). Stored
    /// in the flat <c>nodetemplates</c> collection so the library is data —
    /// listed in Studio, expandable per airline later.
    /// </summary>
    public static class NodeTemplateDefaults
    {
        /// <summary>
        /// "All flights between the US and Dubai" as ONE node: two market zone
        /// params + a direction. Expands to origin/destination in-list filters,
        /// with the reverse-direction pair chained on the fail branch when
        /// bidirectional: (o∈A ∧ d∈B) ∨ (o∈B ∧ d∈A).
        /// </summary>
        public static string MarketFilterJson => """
            {
              "id": "market-filter",
              "name": "Market filter",
              "category": "filter",
              "description": "Passes flight solutions between two markets. Markets are Market Zones (country/region/airport selections built to airport lists); direction can be one-way or bidirectional.",
              "params": [
                { "key": "from", "type": "marketZone", "label": "From market" },
                { "key": "to", "type": "marketZone", "label": "To market" },
                { "key": "direction", "type": "choice", "label": "Direction", "values": ["one-way", "bidirectional"], "default": "bidirectional" }
              ],
              "expansion": {
                "entry": "out",
                "nodes": [
                  { "id": "out", "type": "stringFilter", "label": "origin in From",
                    "config": { "source": { "kind": "request", "path": "flightSolution.origin" },
                                "compare": { "operator": "in", "values": "${zone:from}", "caseInsensitive": true, "trim": true },
                                "arraySelector": "first", "onMissing": "fail" } },
                  { "id": "outDest", "type": "stringFilter", "label": "destination in To",
                    "config": { "source": { "kind": "request", "path": "flightSolution.destination" },
                                "compare": { "operator": "in", "values": "${zone:to}", "caseInsensitive": true, "trim": true },
                                "arraySelector": "first", "onMissing": "fail" } },
                  { "id": "back", "type": "stringFilter", "label": "origin in To (return)", "when": "direction=bidirectional",
                    "config": { "source": { "kind": "request", "path": "flightSolution.origin" },
                                "compare": { "operator": "in", "values": "${zone:to}", "caseInsensitive": true, "trim": true },
                                "arraySelector": "first", "onMissing": "fail" } },
                  { "id": "backDest", "type": "stringFilter", "label": "destination in From (return)", "when": "direction=bidirectional",
                    "config": { "source": { "kind": "request", "path": "flightSolution.destination" },
                                "compare": { "operator": "in", "values": "${zone:from}", "caseInsensitive": true, "trim": true },
                                "arraySelector": "first", "onMissing": "fail" } }
                ],
                "edges": [
                  { "from": "out", "to": "outDest", "branch": "pass" },
                  { "from": "outDest", "to": "$pass", "branch": "pass" },
                  { "from": "out", "to": "back", "branch": "fail", "when": "direction=bidirectional" },
                  { "from": "back", "to": "backDest", "branch": "pass", "when": "direction=bidirectional" },
                  { "from": "backDest", "to": "$pass", "branch": "pass", "when": "direction=bidirectional" }
                ]
              }
            }
            """;

        public static IReadOnlyList<string> All => [MarketFilterJson];
    }
}
