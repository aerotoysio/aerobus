using System.Text.Json;
using System.Text.Json.Nodes;
using AeroBus.Core.Data;

namespace AeroBus.Core.Repositories.PolicyStudio
{
    /// <summary>
    /// DocumentForge-backed store for Policy Studio authoring documents (spaces,
    /// folders, policies, schemas, data references, tests, settings, releases).
    ///
    /// Deliberately schema-tolerant: documents are <see cref="JsonObject"/>s, not
    /// typed records. The store is a pass-through for shapes owned by the Studio
    /// client (content is TipTap JSON, statements embed schema fields); the server
    /// only reads the handful of fields it computes on (id, status, version,
    /// statements). Typed models live where the server evaluates — see
    /// <see cref="Services.PolicyStudio.TestRunner"/>.
    ///
    /// Documents are addressed by their business <c>id</c> field over
    /// <see cref="IDocumentForgeClient"/> (the same string-id, raw-JSON seam the
    /// rules-authoring proxy uses), so DocumentForge's internal <c>_id</c> never
    /// leaks into the Studio contract. All collections are namespaced under the
    /// <c>policystudio.</c> prefix and live in AeroBus's main database.
    /// </summary>
    public sealed class PolicyStudioStore
    {
        public const string Prefix = "policystudio.";

        private readonly IDocumentForgeClient _df;

        public PolicyStudioStore(IDocumentForgeClient df) => _df = df;

        public static string Col(string name) => Prefix + name;

        public static string Escape(string s) => s.Replace("'", "''");

        public static string NewId(string prefix) =>
            $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

        public async Task<List<JsonObject>> ListAsync(string collection, string? where = null, CancellationToken ct = default)
        {
            var sql = $"SELECT * FROM {Col(collection)}" + (where is null ? "" : $" WHERE {where}");
            var rows = await _df.QueryAsync(sql, ct);
            return rows.Select(Clean).ToList();
        }

        public async Task<JsonObject?> GetByIdAsync(string collection, string id, CancellationToken ct = default)
        {
            var doc = await _df.GetByFieldAsync(Col(collection), "id", id, ct);
            return doc is { } el ? Clean(el) : null;
        }

        public async Task<JsonObject> InsertAsync(string collection, JsonObject doc, string idPrefix, CancellationToken ct = default)
        {
            if (doc["id"] is null) doc["id"] = NewId(idPrefix);
            var payload = Strip(doc);
            await _df.InsertAsync(Col(collection), payload.ToJsonString(), ct);
            return payload;
        }

        /// <summary>Replace the document whose domain <c>id</c> matches. Returns null when absent.</summary>
        public async Task<JsonObject?> ReplaceByIdAsync(string collection, string id, JsonObject doc, CancellationToken ct = default)
        {
            doc["id"] = id;
            var payload = Strip(doc);
            var replaced = await _df.ReplaceByFieldAsync(Col(collection), "id", id, payload.ToJsonString(), ct);
            return replaced ? payload : null;
        }

        public Task<bool> DeleteByIdAsync(string collection, string id, CancellationToken ct = default) =>
            _df.DeleteByFieldAsync(Col(collection), "id", id, ct);

        public async Task<int> DeleteWhereAsync(string collection, string where, CancellationToken ct = default)
        {
            var rows = await _df.QueryAsync($"SELECT * FROM {Col(collection)} WHERE {where}", ct);
            var deleted = 0;
            foreach (var row in rows)
            {
                if (row.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String &&
                    await _df.DeleteByFieldAsync(Col(collection), "id", idEl.GetString()!, ct))
                    deleted++;
            }
            return deleted;
        }

        public async Task<int> CountAsync(string collection, CancellationToken ct = default)
        {
            var rows = await _df.QueryAsync($"SELECT * FROM {Col(collection)}", ct);
            return rows.Count;
        }

        /// <summary>Materialise a DF row as a JsonObject without its bookkeeping fields.</summary>
        private static JsonObject Clean(JsonElement el)
        {
            var obj = JsonNode.Parse(el.GetRawText())!.AsObject();
            obj.Remove("_id");
            obj.Remove("_etag");
            return obj;
        }

        /// <summary>Deep-clone without DF bookkeeping fields — safe to return or re-insert.</summary>
        private static JsonObject Strip(JsonObject doc)
        {
            var clone = (JsonObject)doc.DeepClone();
            clone.Remove("_id");
            clone.Remove("_etag");
            return clone;
        }
    }
}
