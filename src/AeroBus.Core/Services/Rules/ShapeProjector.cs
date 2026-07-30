using System.Text.Json;
using System.Text.Json.Nodes;

namespace AeroBus.Core.Services.Rules
{
    /// <summary>
    /// The mapping/extraction layer of docs/rule-based-retailing.md: projects a
    /// caller's request into the CANONICAL policy contract before a policy
    /// evaluates, so one policy serves every request shape.
    ///
    /// A shape field maps to a canonical field via its <c>concept</c> property
    /// (set in the Studio shape editor), or IMPLICITLY when its path equals a
    /// canonical path. Canonical paths under an array root (<c>customers.*</c>)
    /// are zipped into an array of objects; a singleton source (one customer
    /// object) lifts into a one-element array — policies always see arrays.
    /// <c>paxIds</c> is derived from <c>customers[].id</c> for rules that echo
    /// the whole passenger list. The engine never sees any of this — it keeps
    /// evaluating plain primitives on the canonical paths.
    /// </summary>
    public static class ShapeProjector
    {
        /// <summary>Root segments of the canonical contract that are arrays of objects.</summary>
        private static readonly HashSet<string> ArrayRoots = new(StringComparer.Ordinal) { "customers" };

        public static JsonElement Project(JsonElement request, JsonElement shape, JsonElement canonicalShape)
        {
            var canonicalPaths = FieldPaths(canonicalShape);
            var output = new JsonObject();
            var arrayColumns = new Dictionary<string, List<(string Column, List<JsonNode?> Values)>>(StringComparer.Ordinal);

            foreach (var field in Fields(shape))
            {
                var path = GetString(field, "path");
                if (path is null) continue;

                var concept = GetString(field, "concept");
                if (concept is null && canonicalPaths.Contains(path)) concept = path; // implicit identity
                if (concept is null || !canonicalPaths.Contains(concept)) continue;

                var values = Extract(request, path);
                if (values.Count == 0) continue;

                var root = concept.Split('.')[0];
                if (ArrayRoots.Contains(root) && concept.Contains('.'))
                {
                    var column = concept[(root.Length + 1)..];
                    if (!arrayColumns.TryGetValue(root, out var cols))
                        arrayColumns[root] = cols = new List<(string, List<JsonNode?>)>();
                    cols.Add((column, values));
                }
                else
                {
                    SetPath(output, concept, values[0]);
                }
            }

            // Zip per-column value lists into arrays of objects (index-aligned;
            // a single value broadcasts only into element 0 — no fabrication).
            foreach (var (root, cols) in arrayColumns)
            {
                var length = cols.Max(c => c.Values.Count);
                var arr = new JsonArray();
                for (var i = 0; i < length; i++)
                {
                    var obj = new JsonObject();
                    foreach (var (column, values) in cols)
                        if (i < values.Count && values[i] is { } v)
                            SetPath(obj, column, v);
                    arr.Add(obj);
                }
                output[root] = arr;
            }

            // Convenience: rules echo the whole pax list via a flat paxIds array.
            if (output["customers"] is JsonArray customers)
            {
                var paxIds = new JsonArray();
                foreach (var c in customers)
                    if (c?["id"] is { } id)
                        paxIds.Add(id.DeepClone());
                if (paxIds.Count > 0) output["paxIds"] = paxIds;
            }

            return JsonSerializer.Deserialize<JsonElement>(output.ToJsonString());
        }

        /// <summary>
        /// All values at a dotted path, fanning out through arrays. A path that
        /// crosses no array yields at most one value.
        /// </summary>
        internal static List<JsonNode?> Extract(JsonElement request, string path)
        {
            var results = new List<JsonNode?>();
            Walk(request, path.Split('.'), 0, results);
            return results;
        }

        private static void Walk(JsonElement el, string[] segments, int index, List<JsonNode?> results)
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    Walk(item, segments, index, results);
                return;
            }
            if (index >= segments.Length)
            {
                if (el.ValueKind != JsonValueKind.Null && el.ValueKind != JsonValueKind.Undefined)
                    results.Add(JsonNode.Parse(el.GetRawText()));
                return;
            }
            if (el.ValueKind != JsonValueKind.Object) return;
            if (!el.TryGetProperty(segments[index], out var next)) return;
            Walk(next, segments, index + 1, results);
        }

        private static void SetPath(JsonObject target, string path, JsonNode? value)
        {
            var segments = path.Split('.');
            var current = target;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (current[segments[i]] is not JsonObject next)
                    current[segments[i]] = next = new JsonObject();
                current = next;
            }
            current[segments[^1]] = value?.DeepClone();
        }

        private static IEnumerable<JsonElement> Fields(JsonElement shape) =>
            shape.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray()
                : [];

        private static HashSet<string> FieldPaths(JsonElement shape) =>
            Fields(shape)
                .Select(f => GetString(f, "path"))
                .Where(p => p is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

        private static string? GetString(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
    }
}
