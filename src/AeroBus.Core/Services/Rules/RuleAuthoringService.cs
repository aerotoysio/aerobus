using System.Text.Json;
using System.Text.Json.Nodes;
using AeroBus.Core.Data;
using AeroBus.Core.Rules;

namespace AeroBus.Core.Services.Rules
{
    /// <summary>
    /// Thin authoring surface over RuleForge's DocumentForge collections. Works
    /// with raw JSON (rule/reference-set docs use string ids like
    /// <c>"rule-shop-bundles"</c>, not Guid <see cref="Model.IDocument"/>s), so it
    /// goes through <see cref="IDocumentForgeClient"/> directly rather than the
    /// Guid-keyed DocumentRepository. Publishing writes the immutable snapshot,
    /// bumps the rule/binding, then asks RuleForge to refresh. The doc shapes
    /// here match exactly what the RuleForge loader reads (DocumentForgeRuleSource:
    /// rules.currentVersion, ruleversions.{ruleId,version,snapshot},
    /// environments id=<c>env-{name}</c>.ruleBindings).
    /// </summary>
    public sealed class RuleAuthoringService
    {
        public const string RulesCollection = "rules";
        public const string RuleVersionsCollection = "ruleversions";
        public const string EnvironmentsCollection = "environments";
        public const string ReferenceSetsCollection = "referencesets";
        public const string ReferenceSetVersionsCollection = "referencesetversions";

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private readonly IDocumentForgeClient _df;
        private readonly IRuleForgeClient _ruleForge;

        public RuleAuthoringService(IDocumentForgeClient df, IRuleForgeClient ruleForge)
        {
            _df = df;
            _ruleForge = ruleForge;
        }

        // ─── rules ────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<JsonElement>> ListRulesAsync(string? status, CancellationToken ct = default)
        {
            var sql = $"SELECT * FROM {RulesCollection}";
            if (!string.IsNullOrWhiteSpace(status))
                sql += $" WHERE status = '{Escape(status)}'";
            return await _df.QueryAsync(sql, ct);
        }

        public Task<JsonElement?> GetRuleAsync(string id, CancellationToken ct = default) =>
            _df.GetByFieldAsync(RulesCollection, "id", id, ct);

        /// <summary>
        /// Upsert a raw rule document. Minimal validation: the body's id must match
        /// the route, and it must carry endpoint + method + nodes + edges. Returns
        /// the persisted body (with currentVersion preserved from any existing rule).
        /// </summary>
        public async Task<JsonNode> UpsertRuleAsync(string id, JsonNode body, CancellationToken ct = default)
        {
            var obj = body.AsObject();
            ValidateRule(id, obj);

            var existing = await _df.GetByFieldAsync(RulesCollection, "id", id, ct);
            if (existing is { } prev)
            {
                // Preserve currentVersion + status on a raw edit unless the caller set them.
                if (obj["currentVersion"] is null && prev.TryGetProperty("currentVersion", out var cv))
                    obj["currentVersion"] = JsonNode.Parse(cv.GetRawText());
                if (obj["status"] is null && prev.TryGetProperty("status", out var st))
                    obj["status"] = JsonNode.Parse(st.GetRawText());
                await _df.ReplaceByFieldAsync(RulesCollection, "id", id, obj.ToJsonString(), ct);
            }
            else
            {
                obj["currentVersion"] ??= 0;
                obj["status"] ??= "draft";
                await _df.InsertAsync(RulesCollection, obj.ToJsonString(), ct);
            }
            return obj;
        }

        public Task<bool> DeleteRuleAsync(string id, CancellationToken ct = default) =>
            _df.DeleteByFieldAsync(RulesCollection, "id", id, ct);

        /// <summary>
        /// Publish a rule to <paramref name="env"/>: snapshot the current rule into
        /// <c>ruleversions</c> (id <c>rv-{id}-{v}</c>, immutable), bump the rule's
        /// currentVersion + status, bind it in the environment's ruleBindings, then
        /// refresh RuleForge so the next request reads the new version.
        /// </summary>
        public async Task<PublishResult> PublishRuleAsync(string id, string env, CancellationToken ct = default)
        {
            var ruleEl = await _df.GetByFieldAsync(RulesCollection, "id", id, ct)
                         ?? throw new InvalidOperationException($"Rule '{id}' not found.");
            var rule = JsonNode.Parse(ruleEl.GetRawText())!.AsObject();

            var current = ruleEl.TryGetProperty("currentVersion", out var cv) && cv.TryGetInt32(out var v) ? v : 0;
            var next = current + 1;

            // Immutable version snapshot — the loader reads {ruleId, version, snapshot}.
            var snapshot = JsonNode.Parse(rule.ToJsonString())!.AsObject();
            snapshot["currentVersion"] = next;
            snapshot["status"] = "published";
            var versionDoc = new JsonObject
            {
                ["id"] = $"rv-{id}-{next}",
                ["ruleId"] = id,
                ["version"] = next,
                ["snapshot"] = snapshot,
                ["publishedAt"] = DateTime.UtcNow.ToString("o"),
            };
            await UpsertByIdAsync(RuleVersionsCollection, $"rv-{id}-{next}", versionDoc, ct);

            // Bump the editable rule header.
            rule["currentVersion"] = next;
            rule["status"] = "published";
            await _df.ReplaceByFieldAsync(RulesCollection, "id", id, rule.ToJsonString(), ct);

            // Bind in the environment doc (id "env-{name}", ruleBindings {ruleId: version}).
            await BindEnvironmentAsync(env, id, next, ct);

            var refreshed = await _ruleForge.RefreshAsync(ct);
            return new PublishResult(id, next, env, refreshed);
        }

        // ─── reference sets ───────────────────────────────────────────────────

        public Task<JsonElement?> GetReferenceSetAsync(string id, CancellationToken ct = default) =>
            _df.GetByFieldAsync(ReferenceSetsCollection, "id", id, ct);

        public async Task<JsonNode> UpsertReferenceSetAsync(string id, JsonNode body, CancellationToken ct = default)
        {
            var obj = body.AsObject();
            var bodyId = obj["id"]?.GetValue<string>();
            if (!string.Equals(bodyId, id, StringComparison.Ordinal))
                throw new InvalidOperationException($"Reference set id in body ('{bodyId}') must match route ('{id}').");
            if (obj["rows"] is not JsonArray)
                throw new InvalidOperationException("Reference set must have a 'rows' array.");

            var existing = await _df.GetByFieldAsync(ReferenceSetsCollection, "id", id, ct);
            if (existing is not null)
                await _df.ReplaceByFieldAsync(ReferenceSetsCollection, "id", id, obj.ToJsonString(), ct);
            else
            {
                obj["currentVersion"] ??= 0;
                await _df.InsertAsync(ReferenceSetsCollection, obj.ToJsonString(), ct);
            }
            return obj;
        }

        /// <summary>
        /// Publish a reference set into <c>referencesetversions</c> and bump its
        /// currentVersion. The version doc shape matches exactly what the
        /// RuleForge loader reads (DocumentForgeReferenceSetSource):
        /// <c>{refId, version, columns, rows}</c> — the rows are stored flat on the
        /// version doc, NOT nested under a snapshot.
        /// </summary>
        public async Task<PublishResult> PublishReferenceSetAsync(string id, CancellationToken ct = default)
        {
            var refEl = await _df.GetByFieldAsync(ReferenceSetsCollection, "id", id, ct)
                        ?? throw new InvalidOperationException($"Reference set '{id}' not found.");
            var refSet = JsonNode.Parse(refEl.GetRawText())!.AsObject();

            var current = refEl.TryGetProperty("currentVersion", out var cv) && cv.TryGetInt32(out var v) ? v : 0;
            var next = current + 1;

            var columns = refSet["columns"]?.DeepClone() ?? new JsonArray();
            var rows = refSet["rows"]?.DeepClone() ?? new JsonArray();
            var versionDoc = new JsonObject
            {
                ["id"] = $"rsv-{id}-{next}",
                ["refId"] = id,
                ["version"] = next,
                ["columns"] = columns,
                ["rows"] = rows,
                ["publishedAt"] = DateTime.UtcNow.ToString("o"),
            };
            await UpsertByIdAsync(ReferenceSetVersionsCollection, $"rsv-{id}-{next}", versionDoc, ct);

            refSet["currentVersion"] = next;
            await _df.ReplaceByFieldAsync(ReferenceSetsCollection, "id", id, refSet.ToJsonString(), ct);

            var refreshed = await _ruleForge.RefreshAsync(ct);
            return new PublishResult(id, next, Env: null, refreshed);
        }

        // ─── environments ─────────────────────────────────────────────────────

        public Task<JsonElement?> GetEnvironmentAsync(string name, CancellationToken ct = default) =>
            _df.GetByFieldAsync(EnvironmentsCollection, "id", $"env-{name}", ct);

        private async Task BindEnvironmentAsync(string env, string ruleId, int version, CancellationToken ct)
        {
            var envId = $"env-{env}";
            var existing = await _df.GetByFieldAsync(EnvironmentsCollection, "id", envId, ct);

            JsonObject envDoc;
            if (existing is { } el)
            {
                envDoc = JsonNode.Parse(el.GetRawText())!.AsObject();
            }
            else
            {
                envDoc = new JsonObject
                {
                    ["id"] = envId,
                    ["name"] = env,
                    ["ruleBindings"] = new JsonObject(),
                };
            }

            if (envDoc["ruleBindings"] is not JsonObject bindings)
            {
                bindings = new JsonObject();
                envDoc["ruleBindings"] = bindings;
            }
            bindings[ruleId] = version;

            await UpsertByIdAsync(EnvironmentsCollection, envId, envDoc, ct);
        }

        // ─── helpers ──────────────────────────────────────────────────────────

        private async Task UpsertByIdAsync(string collection, string id, JsonNode doc, CancellationToken ct)
        {
            var existing = await _df.GetByFieldAsync(collection, "id", id, ct);
            if (existing is not null)
                await _df.ReplaceByFieldAsync(collection, "id", id, doc.ToJsonString(), ct);
            else
                await _df.InsertAsync(collection, doc.ToJsonString(), ct);
        }

        private static void ValidateRule(string id, JsonObject obj)
        {
            var bodyId = obj["id"]?.GetValue<string>();
            if (!string.Equals(bodyId, id, StringComparison.Ordinal))
                throw new InvalidOperationException($"Rule id in body ('{bodyId}') must match route ('{id}').");
            if (obj["endpoint"] is null) throw new InvalidOperationException("Rule must have an 'endpoint'.");
            if (obj["method"] is null) throw new InvalidOperationException("Rule must have a 'method'.");
            if (obj["nodes"] is not JsonArray) throw new InvalidOperationException("Rule must have a 'nodes' array.");
            if (obj["edges"] is not JsonArray) throw new InvalidOperationException("Rule must have an 'edges' array.");
        }

        private static string Escape(string s) => s.Replace("'", "''");
    }

    public sealed record PublishResult(string RuleId, int Version, string? Env, bool RuleForgeRefreshed);
}
