using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AeroBus.Core.Data
{
    /// <summary>
    /// Typed document access for Guid-keyed aggregates, layered over
    /// <see cref="IDocumentForgeClient"/>. Documents are addressed by their
    /// business <c>Id</c> field (never DocumentForge's internal <c>_id</c> — that
    /// stays an implementation detail of the CAS paths that need it).
    /// </summary>
    public interface IDocumentStore
    {
        Task<T?> GetByIdAsync<T>(string collection, Guid id, CancellationToken ct = default);

        Task<T?> GetByFieldAsync<T>(string collection, string field, string value, CancellationToken ct = default);

        /// <summary>Everything in the collection.</summary>
        Task<IReadOnlyList<T>> QueryAsync<T>(string collection, CancellationToken ct = default);

        /// <summary>Equality-filtered query; null page/size returns everything.</summary>
        Task<IReadOnlyList<T>> QueryAsync<T>(
            string collection,
            Dictionary<string, object?> filters,
            int? page = null,
            int? size = null,
            CancellationToken ct = default);

        /// <summary>Raw WHERE-clause query (may carry ORDER BY); null page/size returns everything.</summary>
        Task<IReadOnlyList<T>> QueryWhereAsync<T>(
            string collection,
            string where,
            int? page = null,
            int? size = null,
            CancellationToken ct = default);

        Task<int> CountAsync(string collection, CancellationToken ct = default);

        Task<int> CountAsync(string collection, Dictionary<string, object?> filters, CancellationToken ct = default);

        /// <summary>
        /// Insert-or-replace by business Id, stamping <c>Created</c> (first write)
        /// and <c>Updated</c> (every write) when the document carries those fields.
        /// Returns the persisted document.
        /// </summary>
        Task<T?> UpsertAsync<T>(string collection, T model, Guid id, CancellationToken ct = default);

        Task<bool> DeleteAsync(string collection, Guid id, CancellationToken ct = default);
    }

    public sealed class DocumentStore(IDocumentForgeClient client) : IDocumentStore
    {
        // Storage convention: documents are persisted with camelCase keys
        // (id, companyId, …) — matching the HTTP wire, PolicyStudio and the
        // RuleForge contract. Case-insensitive reads keep the PascalCase .NET
        // models hydrating unchanged. Every field name interpolated into SQL /
        // filters / CAS must therefore be written camelCase too.
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly IDocumentForgeClient _client = client;

        public async Task<T?> GetByIdAsync<T>(string collection, Guid id, CancellationToken ct = default)
        {
            var el = await _client.GetByFieldAsync(collection, Df.Id, id.ToString(), ct);
            return el is { } e ? e.Deserialize<T>(Json) : default;
        }

        public async Task<T?> GetByFieldAsync<T>(string collection, string field, string value, CancellationToken ct = default)
        {
            var el = await _client.GetByFieldAsync(collection, field, value, ct);
            return el is { } e ? e.Deserialize<T>(Json) : default;
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string collection, CancellationToken ct = default) =>
            QueryWhereAsync<T>(collection, string.Empty, ct: ct);

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            string collection,
            Dictionary<string, object?> filters,
            int? page = null,
            int? size = null,
            CancellationToken ct = default) =>
            QueryWhereAsync<T>(collection, BuildWhere(filters), page, size, ct);

        public async Task<IReadOnlyList<T>> QueryWhereAsync<T>(
            string collection,
            string where,
            int? page = null,
            int? size = null,
            CancellationToken ct = default)
        {
            var sql = $"SELECT * FROM {collection}";
            if (!string.IsNullOrWhiteSpace(where)) sql += $" WHERE {where}";

            // Paging is pushed to the server: DocumentForge applies OFFSET after
            // ORDER BY and before LIMIT (skip-then-take), so page p of size s is
            // exactly the old client-side Skip((p-1)*s).Take(s) slice.
            if (page is { } p && size is { } s && p > 0 && s > 0)
            {
                sql += $" LIMIT {s}";
                if (p > 1) sql += $" OFFSET {(p - 1) * s}";
            }
            else if (size is { } onlySize && onlySize > 0)
            {
                sql += $" LIMIT {onlySize}";
            }

            var rows = await _client.QueryAsync(sql, ct);

            var list = new List<T>(rows.Count);
            foreach (var row in rows)
            {
                var item = row.Deserialize<T>(Json);
                if (item is not null) list.Add(item);
            }
            return list;
        }

        public Task<int> CountAsync(string collection, CancellationToken ct = default) =>
            CountAsync(collection, new Dictionary<string, object?>(), ct);

        public async Task<int> CountAsync(string collection, Dictionary<string, object?> filters, CancellationToken ct = default)
        {
            // Server-side aggregate: COUNT(*) returns a single { count: N } row
            // (and tolerates a not-yet-created collection by returning 0).
            var sql = $"SELECT COUNT(*) FROM {collection}";
            var where = BuildWhere(filters);
            if (!string.IsNullOrWhiteSpace(where)) sql += $" WHERE {where}";
            var rows = await _client.QueryAsync(sql, ct);
            if (rows.Count == 0) return 0;
            return rows[0].TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number
                ? (int)n.GetInt64()
                : 0;
        }

        public async Task<T?> UpsertAsync<T>(string collection, T model, Guid id, CancellationToken ct = default)
        {
            var node = JsonSerializer.SerializeToNode(model, Json)?.AsObject()
                       ?? throw new InvalidOperationException($"Model of type {typeof(T).Name} did not serialize to an object.");

            // A save without a business Id is a create: assign one (callers read
            // it back off the returned document — e.g. catalogue /save flows).
            if (id == Guid.Empty)
            {
                id = Guid.NewGuid();
                node[Df.Id] = id.ToString();
            }

            // Stamp audit fields where the document carries them: Updated on every
            // write; Created is preserved from the stored document when the caller
            // omits it (a full-document admin edit must not wipe the Created date).
            var existing = await _client.GetByFieldAsync(collection, Df.Id, id.ToString(), ct);
            var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (!node.TryGetPropertyValue(Df.Created, out var created) || created is null)
            {
                string? preserved = null;
                if (existing is { } prev &&
                    prev.TryGetProperty(Df.Created, out var prevCreated) &&
                    prevCreated.ValueKind == JsonValueKind.String)
                    preserved = prevCreated.GetString();
                node[Df.Created] = preserved ?? now;
            }
            node[Df.Updated] = now;

            var json = node.ToJsonString();
            if (existing is not null)
                await _client.ReplaceByFieldAsync(collection, Df.Id, id.ToString(), json, ct);
            else
                await _client.InsertAsync(collection, json, ct);

            return node.Deserialize<T>(Json);
        }

        public Task<bool> DeleteAsync(string collection, Guid id, CancellationToken ct = default) =>
            _client.DeleteByFieldAsync(collection, Df.Id, id.ToString(), ct);

        // ── SQL helpers ────────────────────────────────────────────────────────

        private static string BuildWhere(Dictionary<string, object?> filters)
        {
            if (filters.Count == 0) return string.Empty;
            var clauses = new List<string>(filters.Count);
            foreach (var (field, value) in filters)
                clauses.Add(value is null ? $"{field} IS NULL" : $"{field} = {Literal(value)}");
            return string.Join(" AND ", clauses);
        }

        private static string Literal(object value) => value switch
        {
            bool b => b ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
            DateTime dt => $"'{dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)}'",
            DateTimeOffset dto => $"'{dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)}'",
            _ => $"'{value.ToString()?.Replace("'", "''")}'",
        };
    }
}
