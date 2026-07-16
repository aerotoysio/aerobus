using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Data
{
    /// <summary>One guard in a conditional (compare-and-set) update — e.g.
    /// <c>("Available", "&gt;=", 2)</c> or the value-less <c>("Seq", "exists")</c>.</summary>
    public sealed record DocumentForgeCondition(
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("op")] string Op,
        [property: JsonPropertyName("value")] object? Value = null);

    /// <summary>One mutation applied when every condition holds — e.g.
    /// <c>("Available", "dec", 2)</c> or <c>("Status", "set", "Dispatching")</c>.</summary>
    public sealed record DocumentForgeMutation(
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("op")] string Op,
        [property: JsonPropertyName("value")] object? Value = null);

    /// <summary>
    /// Outcome of a conditional update. <see cref="Document"/> carries the
    /// post-mutation document on success (the CAS read-back — e.g. the
    /// incremented Seq); <see cref="FailedCondition"/> is set on a 409.
    /// </summary>
    public sealed record DocumentForgeCasResult(
        bool Success,
        int StatusCode,
        string? FailedCondition = null,
        JsonElement? Document = null);

    /// <summary>Reachability probe result for GET /health.</summary>
    public sealed record DocumentForgeHealth(bool Reachable, int StatusCode, string? Error = null);

    /// <summary>
    /// Raw JSON surface of the DocumentForge HTTP API — collection inserts,
    /// by-field lookups/replaces/deletes, SQL queries (rows surface the internal
    /// <c>_id</c>) and the atomic conditional-update primitive. The typed
    /// <see cref="IDocumentStore"/> sits on top of this for Guid-keyed aggregates;
    /// services needing string-id docs or CAS semantics use this client directly.
    /// </summary>
    public interface IDocumentForgeClient
    {
        Task<DocumentForgeHealth> HealthAsync(CancellationToken ct = default);

        /// <summary>POST /databases {name, createIfMissing:true}. Idempotent —
        /// an already-attached database is success. Server-level (not db-scoped);
        /// used by tenant provisioning to create an org's database. False on failure.</summary>
        Task<bool> EnsureDatabaseAsync(string database, CancellationToken ct = default);

        /// <summary>POST /collections/{collection}. Returns the new internal _id, or null on failure.</summary>
        Task<string?> InsertAsync(string collection, string json, CancellationToken ct = default);

        /// <summary>GET /collections/{collection}/by/{field}/{value} → the document, or null (404).</summary>
        Task<JsonElement?> GetByFieldAsync(string collection, string field, string value, CancellationToken ct = default);

        /// <summary>PUT /collections/{collection}/by/{field}/{value} — full replace. False when no match.</summary>
        Task<bool> ReplaceByFieldAsync(string collection, string field, string value, string json, CancellationToken ct = default);

        /// <summary>DELETE /collections/{collection}/by/{field}/{value}. False when no match.</summary>
        Task<bool> DeleteByFieldAsync(string collection, string field, string value, CancellationToken ct = default);

        /// <summary>POST /query. Returns the documents array (each row carries <c>_id</c>).</summary>
        Task<IReadOnlyList<JsonElement>> QueryAsync(string sql, CancellationToken ct = default);

        /// <summary>
        /// POST /collections/{collection}/{docId}/conditional — apply
        /// <paramref name="operations"/> iff every <paramref name="conditions"/>
        /// holds, atomically under the engine write lock. 409 → guard failed,
        /// 404 → unknown _id; neither throws.
        /// </summary>
        Task<DocumentForgeCasResult> ConditionalUpdateAsync(
            string collection,
            string docId,
            IReadOnlyList<DocumentForgeCondition> conditions,
            IReadOnlyList<DocumentForgeMutation> operations,
            CancellationToken ct = default);
    }

    public sealed class DocumentForgeClient : IDocumentForgeClient
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _http;
        // Resolved PER CALL so the same client can route to a different database
        // each request — the tenant registration supplies a provider that reads the
        // caller's org database; keyed (rules/control) registrations supply a fixed
        // value. Returning null/empty targets the server's default database.
        private readonly Func<string?> _databaseProvider;

        public DocumentForgeClient(HttpClient http, IOptions<DocumentForgeOptions> options)
            : this(http, options.Value, options.Value.Database) { }

        /// <summary>Fixed-database constructor (null = the server's default database
        /// over the flat routes) — used by the keyed rules/control clients and the
        /// provisioning store factory, which target one specific database.</summary>
        internal DocumentForgeClient(HttpClient http, DocumentForgeOptions opts, string? database)
            : this(http, opts, () => database) { }

        /// <summary>Per-request database constructor — the provider is consulted on
        /// every data-plane call. The main (tenant-routed) client uses this with a
        /// provider that reads <c>ITenantDatabase.CurrentDatabase</c>.</summary>
        internal DocumentForgeClient(HttpClient http, DocumentForgeOptions opts, Func<string?> databaseProvider)
        {
            _http = http;
            _databaseProvider = databaseProvider;
            _http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
        }

        /// <summary>
        /// Route a data-plane path. With a named database the scoped
        /// <c>/db/{name}/…</c> surface is used (the flat routes always hit the
        /// server's default database — X-Database is not honoured on writes).
        /// </summary>
        private string DataUrl(string relative)
        {
            var database = _databaseProvider();
            return string.IsNullOrWhiteSpace(database) ? relative : $"db/{Uri.EscapeDataString(database)}/{relative}";
        }

        public async Task<bool> EnsureDatabaseAsync(string database, CancellationToken ct = default)
        {
            // Server-level route (NOT db-scoped): create the database if missing.
            var body = JsonSerializer.Serialize(new { name = database, createIfMissing = true }, Json);
            using var resp = await _http.PostAsync("databases", new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (resp.IsSuccessStatusCode) return true;

            // Tolerate "already attached/exists" as success (idempotent).
            var text = await resp.Content.ReadAsStringAsync(ct);
            return text.Contains("already", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("exists", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<DocumentForgeHealth> HealthAsync(CancellationToken ct = default)
        {
            try
            {
                var resp = await _http.GetAsync("health", ct);
                return new DocumentForgeHealth(resp.IsSuccessStatusCode, (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                return new DocumentForgeHealth(false, 0, ex.Message);
            }
        }

        public async Task<string?> InsertAsync(string collection, string json, CancellationToken ct = default)
        {
            var resp = await _http.PostAsync(
                DataUrl($"collections/{Uri.EscapeDataString(collection)}"),
                new StringContent(json, Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = await ReadJsonAsync(resp, ct);
            return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }

        public async Task<JsonElement?> GetByFieldAsync(string collection, string field, string value, CancellationToken ct = default)
        {
            // The scoped surface has no by-field routes; a SELECT is equivalent
            // (and honours indexes the same way the flat by-field route does).
            if (!string.IsNullOrWhiteSpace(_databaseProvider()))
            {
                var rows = await QueryAsync(
                    $"SELECT * FROM {collection} WHERE {field} = '{Escape(value)}' LIMIT 1", ct);
                return rows.Count > 0 ? rows[0] : null;
            }

            var resp = await _http.GetAsync(ByFieldUrl(collection, field, value), ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            using var doc = await ReadJsonAsync(resp, ct);
            return doc.RootElement.TryGetProperty("document", out var el)
                ? el.Clone()
                : null;
        }

        public async Task<bool> ReplaceByFieldAsync(string collection, string field, string value, string json, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(_databaseProvider()))
            {
                // Resolve the internal _id, then PUT the scoped by-id route.
                var existing = await GetByFieldAsync(collection, field, value, ct);
                if (existing is not { } row ||
                    !row.TryGetProperty("_id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    return false;
                var putResp = await _http.PutAsync(
                    DataUrl($"collections/{Uri.EscapeDataString(collection)}/{idEl.GetString()}"),
                    new StringContent(json, Encoding.UTF8, "application/json"), ct);
                if (putResp.StatusCode == HttpStatusCode.NotFound) return false;
                putResp.EnsureSuccessStatusCode();
                return true;
            }

            var resp = await _http.PutAsync(
                ByFieldUrl(collection, field, value),
                new StringContent(json, Encoding.UTF8, "application/json"), ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return false;
            resp.EnsureSuccessStatusCode();
            return true;
        }

        public async Task<bool> DeleteByFieldAsync(string collection, string field, string value, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(_databaseProvider()))
            {
                var affected = await ExecuteAsync(
                    $"DELETE FROM {collection} WHERE {field} = '{Escape(value)}'", ct);
                return affected > 0;
            }

            var resp = await _http.DeleteAsync(ByFieldUrl(collection, field, value), ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return false;
            resp.EnsureSuccessStatusCode();
            using var doc = await ReadJsonAsync(resp, ct);
            return !doc.RootElement.TryGetProperty("deletedCount", out var n) || n.GetInt64() > 0;
        }

        /// <summary>Run a mutating SQL statement; returns the affected count.</summary>
        private async Task<long> ExecuteAsync(string sql, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new { sql }, Json);
            var resp = await _http.PostAsync(DataUrl("query"), new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (resp.StatusCode == HttpStatusCode.BadRequest)
            {
                using var err = await ReadJsonAsync(resp, ct);
                if (err.RootElement.TryGetProperty("code", out var code) &&
                    string.Equals(code.GetString(), "collectionNotFound", StringComparison.OrdinalIgnoreCase))
                    return 0;
                var message = err.RootElement.TryGetProperty("error", out var msg) ? msg.GetString() : null;
                throw new HttpRequestException($"DocumentForge statement failed: {message ?? "bad request"} (sql: {sql})");
            }
            resp.EnsureSuccessStatusCode();
            using var doc = await ReadJsonAsync(resp, ct);
            return doc.RootElement.TryGetProperty("affected", out var n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt64()
                : 0;
        }

        private static string Escape(string s) => s.Replace("'", "''");

        public async Task<IReadOnlyList<JsonElement>> QueryAsync(string sql, CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(new { sql }, Json);
            var resp = await _http.PostAsync(DataUrl("query"), new StringContent(body, Encoding.UTF8, "application/json"), ct);

            if (resp.StatusCode == HttpStatusCode.BadRequest)
            {
                // Collections are created lazily on first insert; reading one that
                // doesn't exist yet is an EMPTY result, not an error (issue #69's
                // stable `code` lets us branch without string-matching).
                using var err = await ReadJsonAsync(resp, ct);
                if (err.RootElement.TryGetProperty("code", out var code) &&
                    code.ValueKind == JsonValueKind.String &&
                    string.Equals(code.GetString(), "collectionNotFound", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<JsonElement>();
                var message = err.RootElement.TryGetProperty("error", out var msg) ? msg.GetString() : null;
                throw new HttpRequestException($"DocumentForge query failed: {message ?? "bad request"} (sql: {sql})");
            }

            resp.EnsureSuccessStatusCode();
            using var doc = await ReadJsonAsync(resp, ct);
            if (!doc.RootElement.TryGetProperty("documents", out var docs) || docs.ValueKind != JsonValueKind.Array)
                return Array.Empty<JsonElement>();
            var list = new List<JsonElement>(docs.GetArrayLength());
            foreach (var el in docs.EnumerateArray())
                list.Add(el.Clone());
            return list;
        }

        public async Task<DocumentForgeCasResult> ConditionalUpdateAsync(
            string collection,
            string docId,
            IReadOnlyList<DocumentForgeCondition> conditions,
            IReadOnlyList<DocumentForgeMutation> operations,
            CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(new { conditions, operations }, Json);
            var resp = await _http.PostAsync(
                DataUrl($"collections/{Uri.EscapeDataString(collection)}/{Uri.EscapeDataString(docId)}/conditional"),
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            var status = (int)resp.StatusCode;
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return new DocumentForgeCasResult(false, status);

            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                using var conflictDoc = await ReadJsonAsync(resp, ct);
                var failed = conflictDoc.RootElement.TryGetProperty("failedCondition", out var fc) && fc.ValueKind == JsonValueKind.String
                    ? fc.GetString()
                    : null;
                return new DocumentForgeCasResult(false, status, failed);
            }

            resp.EnsureSuccessStatusCode();
            using var okDoc = await ReadJsonAsync(resp, ct);
            JsonElement? document = okDoc.RootElement.TryGetProperty("document", out var el)
                ? el.Clone()
                : null;
            return new DocumentForgeCasResult(true, status, Document: document);
        }

        private static string ByFieldUrl(string collection, string field, string value) =>
            $"collections/{Uri.EscapeDataString(collection)}/by/{Uri.EscapeDataString(field)}/{Uri.EscapeDataString(value)}";

        private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
    }
}
