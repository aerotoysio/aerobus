using System.Text.Json.Nodes;
using AeroBus.Core.Events;
using AeroBus.Core.Repositories.PolicyStudio;
using AeroBus.Core.Services.Rules;

namespace AeroBus.Core.Services.PolicyStudio
{
    /// <summary>
    /// The Policy Studio backend logic: the authoring content lifecycle (spaces,
    /// folders, policies, schemas, data references, tests, settings) plus the two
    /// compute surfaces — evaluate test cases against a policy's statements
    /// (<see cref="TestRunner"/>) and compile statements to a RuleForge rule graph
    /// (<see cref="RuleCompiler"/>).
    ///
    /// Publishing does not re-implement the engine release: it compiles the policy,
    /// then hands the rule graph to the shared <see cref="RuleAuthoringService"/>,
    /// which snapshots an immutable version, binds the environment and refreshes
    /// RuleForge — so a published policy goes live on the same engine the rest of
    /// AeroBus calls. Policy Studio content is global (platform-level), so events
    /// are emitted against the global cursor (companyId null).
    /// </summary>
    public sealed class PolicyStudioService
    {
        private const string DefaultEnv = "dev";

        private readonly PolicyStudioStore _store;
        private readonly RuleAuthoringService _ruleAuthoring;
        private readonly IEventPublisher _events;

        public PolicyStudioService(
            PolicyStudioStore store, RuleAuthoringService ruleAuthoring, IEventPublisher events)
        {
            _store = store;
            _ruleAuthoring = ruleAuthoring;
            _events = events;
        }

        // ─── tree ──────────────────────────────────────────────────────────────

        public sealed record PolicyTree(List<JsonObject> Spaces, List<JsonObject> Folders, List<JsonObject> Policies);

        public async Task<PolicyTree> GetTreeAsync(CancellationToken ct = default)
        {
            var spaces = await _store.ListAsync("spaces", ct: ct);
            var folders = await _store.ListAsync("folders", ct: ct);
            var policies = await _store.ListAsync("policies", ct: ct);
            foreach (var p in policies)
            {
                p.Remove("content");
                p.Remove("statements");
            }
            return new PolicyTree(spaces, folders, policies);
        }

        // ─── spaces / folders ────────────────────────────────────────────────────

        public Task<JsonObject> CreateSpaceAsync(JsonObject body, CancellationToken ct = default) =>
            _store.InsertAsync("spaces", body, "sp", ct);

        public Task<JsonObject> CreateFolderAsync(JsonObject body, CancellationToken ct = default) =>
            _store.InsertAsync("folders", body, "f", ct);

        public async Task<JsonObject?> UpdateFolderAsync(string id, JsonObject body, CancellationToken ct = default)
        {
            var existing = await _store.GetByIdAsync("folders", id, ct);
            if (existing is null) return null;
            foreach (var key in new[] { "name", "spaceId" })
                if (body[key] is not null) existing[key] = body[key]!.DeepClone();
            return await _store.ReplaceByIdAsync("folders", id, existing, ct);
        }

        public Task<bool> DeleteFolderAsync(string id, CancellationToken ct = default) =>
            _store.DeleteByIdAsync("folders", id, ct);

        // ─── policies ────────────────────────────────────────────────────────────

        public Task<JsonObject> CreatePolicyAsync(JsonObject body, CancellationToken ct = default)
        {
            var doc = new JsonObject
            {
                ["folderId"] = body["folderId"]?.DeepClone(),
                ["title"] = body["title"]?.DeepClone() ?? "Untitled policy",
                ["summary"] = body["summary"]?.DeepClone() ?? "",
                ["status"] = "draft",
                ["version"] = 1,
                ["versions"] = new JsonArray(),
                ["owner"] = body["owner"]?.DeepClone() ?? "Policy Studio",
                ["updatedAt"] = Today(),
                ["content"] = body["content"]?.DeepClone() ?? DefaultContent(),
            };
            return _store.InsertAsync("policies", doc, "d", ct);
        }

        public Task<JsonObject?> GetPolicyAsync(string id, CancellationToken ct = default) =>
            _store.GetByIdAsync("policies", id, ct);

        public async Task<JsonObject?> UpdatePolicyAsync(string id, JsonObject body, CancellationToken ct = default)
        {
            var existing = await _store.GetByIdAsync("policies", id, ct);
            if (existing is null) return null;

            var contentChanged = body["content"] is not null;
            foreach (var key in new[] { "title", "summary", "content", "statements", "schemaId", "folderId", "drift" })
                if (body[key] is not null) existing[key] = body[key]!.DeepClone();

            // Published documents are immutable — editing content opens the next draft.
            if (contentChanged && existing["status"]?.GetValue<string>() == "published")
            {
                existing["status"] = "draft";
                existing["version"] = (existing["version"]?.GetValue<int>() ?? 1) + 1;
            }
            existing["updatedAt"] = Today();
            return await _store.ReplaceByIdAsync("policies", id, existing, ct);
        }

        /// <summary>Returns false when the policy did not exist; cascades test deletion.</summary>
        public async Task<bool> DeletePolicyAsync(string id, CancellationToken ct = default)
        {
            var deleted = await _store.DeleteByIdAsync("policies", id, ct);
            if (!deleted) return false;
            await _store.DeleteWhereAsync("tests", $"policyId = '{PolicyStudioStore.Escape(id)}'", ct);
            return true;
        }

        /// <summary>
        /// Publish = freeze the policy version AND cut an engine release. The
        /// statements compile to a RuleForge rule graph; the shared
        /// <see cref="RuleAuthoringService"/> then snapshots an immutable version,
        /// binds the <c>dev</c> environment and refreshes RuleForge. A compile error
        /// throws <see cref="RuleCompiler.CompileException"/> (surface as 422) and
        /// blocks the publish; parse <em>warnings</em> still publish. Returns null
        /// when the policy is absent, else the updated policy document.
        /// </summary>
        public async Task<JsonObject?> PublishPolicyAsync(string id, JsonObject? body, CancellationToken ct = default)
        {
            var existing = await _store.GetByIdAsync("policies", id, ct);
            if (existing is null) return null;

            var endpoint = existing["endpoint"]?.GetValue<string>()
                           ?? "/v1/policies/" + Slug(existing["title"]?.GetValue<string>() ?? id);

            // May throw CompileException — callers map that to 422.
            var compiled = RuleCompiler.Compile(existing, id, endpoint);

            var policyVersion = existing["version"]?.GetValue<int>() ?? 1;
            var ruleId = $"rule-{id}";

            // Hand the graph to the shared release path. Strip currentVersion/status so
            // RuleAuthoringService owns engine version state (it bumps, snapshots the
            // immutable ruleversion, binds the env, and refreshes the engine).
            var graph = (JsonObject)compiled.Rule.DeepClone();
            graph.Remove("currentVersion");
            graph.Remove("status");
            await _ruleAuthoring.UpsertRuleAsync(ruleId, graph, ct);
            var publish = await _ruleAuthoring.PublishRuleAsync(ruleId, DefaultEnv, ct);

            // Policy Studio's own release history.
            var release = new JsonObject
            {
                ["policyId"] = id,
                ["policyVersion"] = policyVersion,
                ["ruleId"] = ruleId,
                ["ruleVersion"] = publish.Version,
                ["endpoint"] = endpoint,
                ["env"] = publish.Env,
                ["note"] = body?["note"]?.DeepClone() ?? "",
                ["warnings"] = new JsonArray(compiled.Warnings.Select(w => (JsonNode)w).ToArray()),
                ["refreshed"] = publish.RuleForgeRefreshed,
                ["publishedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            };
            release = await _store.InsertAsync("releases", release, "rel", ct);

            // Policy bookkeeping — supersede the previously-published version.
            if (existing["versions"] is not JsonArray versions)
            {
                versions = new JsonArray();
                existing["versions"] = versions;
            }
            foreach (var v in versions)
                if (v is JsonObject vo && vo["status"]?.GetValue<string>() == "published")
                    vo["status"] = "superseded";
            versions.Add(new JsonObject
            {
                ["v"] = policyVersion,
                ["date"] = Today(),
                ["author"] = existing["owner"]?.DeepClone() ?? "Policy Studio",
                ["note"] = body?["note"]?.DeepClone() ?? "",
                ["status"] = "published",
            });

            existing["status"] = "published";
            existing["drift"] = false;
            existing["endpoint"] = endpoint;
            existing["lastRelease"] = release.DeepClone();
            existing["updatedAt"] = Today();
            var saved = await _store.ReplaceByIdAsync("policies", id, existing, ct);

            await _events.PublishAsync(
                "policy.published",
                new EventSubject("policystudio.policies", id),
                new
                {
                    policyId = id,
                    policyVersion,
                    ruleId,
                    ruleVersion = publish.Version,
                    endpoint,
                    env = publish.Env,
                    refreshed = publish.RuleForgeRefreshed,
                },
                companyId: null, actor: "policy-studio", ct);

            return saved;
        }

        public sealed record CompiledPreview(JsonObject Rule, List<string> Warnings, string Endpoint);

        /// <summary>Compile preview — the rule graph a publish would release, without
        /// releasing. Null when the policy is absent; throws
        /// <see cref="RuleCompiler.CompileException"/> on a compile error.</summary>
        public async Task<CompiledPreview?> CompilePreviewAsync(string id, CancellationToken ct = default)
        {
            var policy = await _store.GetByIdAsync("policies", id, ct);
            if (policy is null) return null;
            var endpoint = policy["endpoint"]?.GetValue<string>()
                           ?? "/v1/policies/" + Slug(policy["title"]?.GetValue<string>() ?? id);
            var compiled = RuleCompiler.Compile(policy, id, endpoint);
            return new CompiledPreview(compiled.Rule, compiled.Warnings, endpoint);
        }

        public async Task<List<JsonObject>> ListReleasesAsync(CancellationToken ct = default)
        {
            var releases = await _store.ListAsync("releases", ct: ct);
            return releases
                .OrderByDescending(r => r["publishedAt"]?.GetValue<string>() ?? "")
                .ToList();
        }

        // ─── schemas ─────────────────────────────────────────────────────────────

        public Task<List<JsonObject>> ListSchemasAsync(CancellationToken ct = default) =>
            _store.ListAsync("schemas", ct: ct);

        public Task<JsonObject> CreateSchemaAsync(JsonObject body, CancellationToken ct = default) =>
            _store.InsertAsync("schemas", body, "sch", ct);

        public Task<JsonObject?> ReplaceSchemaAsync(string id, JsonObject body, CancellationToken ct = default) =>
            _store.ReplaceByIdAsync("schemas", id, body, ct);

        public Task<bool> DeleteSchemaAsync(string id, CancellationToken ct = default) =>
            _store.DeleteByIdAsync("schemas", id, ct);

        // ─── data references ─────────────────────────────────────────────────────

        public Task<List<JsonObject>> ListDataRefsAsync(string? policyId, CancellationToken ct = default)
        {
            var where = policyId is null
                ? null
                : $"scope = 'global' OR policyId = '{PolicyStudioStore.Escape(policyId)}'";
            return _store.ListAsync("datarefs", where, ct);
        }

        public Task<JsonObject> CreateDataRefAsync(JsonObject body, CancellationToken ct = default) =>
            _store.InsertAsync("datarefs", body, "ref", ct);

        public Task<JsonObject?> ReplaceDataRefAsync(string id, JsonObject body, CancellationToken ct = default) =>
            _store.ReplaceByIdAsync("datarefs", id, body, ct);

        public Task<bool> DeleteDataRefAsync(string id, CancellationToken ct = default) =>
            _store.DeleteByIdAsync("datarefs", id, ct);

        // ─── tests ───────────────────────────────────────────────────────────────

        public Task<List<JsonObject>> ListTestsAsync(string policyId, CancellationToken ct = default) =>
            _store.ListAsync("tests", $"policyId = '{PolicyStudioStore.Escape(policyId)}'", ct);

        public Task<JsonObject> CreateTestAsync(string policyId, JsonObject body, CancellationToken ct = default)
        {
            body["policyId"] = policyId;
            return _store.InsertAsync("tests", body, "t", ct);
        }

        public Task<JsonObject?> ReplaceTestAsync(string testId, JsonObject body, CancellationToken ct = default) =>
            _store.ReplaceByIdAsync("tests", testId, body, ct);

        public Task<bool> DeleteTestAsync(string testId, CancellationToken ct = default) =>
            _store.DeleteByIdAsync("tests", testId, ct);

        /// <summary>Run some/all of a policy's tests, persisting each test's
        /// <c>lastResult</c>. Null when the policy is absent.</summary>
        public async Task<List<JsonObject>?> RunTestsAsync(string policyId, JsonObject? body, CancellationToken ct = default)
        {
            var policy = await _store.GetByIdAsync("policies", policyId, ct);
            if (policy is null) return null;

            var statements = policy["statements"] as JsonArray ?? new JsonArray();
            var tests = await _store.ListAsync("tests", $"policyId = '{PolicyStudioStore.Escape(policyId)}'", ct);

            var requested = (body?["testIds"] as JsonArray)?
                .Select(n => n?.GetValue<string>())
                .Where(s => s is not null)
                .ToHashSet();
            if (requested is { Count: > 0 })
                tests = tests.Where(t => requested.Contains(t["id"]?.GetValue<string>())).ToList();

            var ranAt = DateTimeOffset.UtcNow.ToString("O");
            var results = new List<JsonObject>();

            foreach (var test in tests)
            {
                var testId = test["id"]?.GetValue<string>() ?? "?";
                JsonObject result;
                try
                {
                    var input = test["input"] as JsonObject ?? new JsonObject();
                    var evalDate = test["evaluationDate"]?.GetValue<string>() is { } d
                        ? DateOnly.Parse(d)
                        : DateOnly.FromDateTime(DateTime.UtcNow);

                    var run = TestRunner.Run(statements, input, evalDate);
                    var expected = test["expect"]?["verdict"]?.GetValue<string>() ?? "VALID";

                    result = new JsonObject
                    {
                        ["testId"] = testId,
                        ["status"] = run.Verdict == expected ? "pass" : "fail",
                        ["verdict"] = run.Verdict,
                        ["statements"] = new JsonArray(run.Statements.Select(s => (JsonNode)new JsonObject
                        {
                            ["id"] = s.Id,
                            ["text"] = s.Text,
                            ["pass"] = s.Pass,
                            ["conditions"] = new JsonArray(s.Conditions.Select(c => (JsonNode)new JsonObject
                            {
                                ["expr"] = c.Expr,
                                ["pass"] = c.Pass,
                                ["actual"] = c.Actual,
                            }).ToArray()),
                        }).ToArray()),
                        ["ranAt"] = ranAt,
                    };
                }
                catch (Exception ex)
                {
                    result = new JsonObject
                    {
                        ["testId"] = testId,
                        ["status"] = "error",
                        ["statements"] = new JsonArray(),
                        ["error"] = ex.Message,
                        ["ranAt"] = ranAt,
                    };
                }

                test["lastResult"] = result.DeepClone();
                await _store.ReplaceByIdAsync("tests", testId, test, ct);
                results.Add(result);
            }

            return results;
        }

        // ─── settings ────────────────────────────────────────────────────────────

        public async Task<JsonObject> GetSettingsAsync(CancellationToken ct = default) =>
            await _store.GetByIdAsync("settings", "global", ct) ?? new JsonObject { ["id"] = "global" };

        public async Task<JsonObject> SaveSettingsAsync(JsonObject body, CancellationToken ct = default)
        {
            body["id"] = "global";
            return await _store.ReplaceByIdAsync("settings", "global", body, ct)
                   ?? await _store.InsertAsync("settings", body, "settings", ct);
        }

        // ─── seed ────────────────────────────────────────────────────────────────
        //
        // Idempotent dev seeding: each collection in the payload is only written when
        // currently empty. force=true wipes and rewrites the payload's collections.

        public async Task<JsonObject> SeedAsync(JsonObject body, bool force, CancellationToken ct = default)
        {
            var report = new JsonObject();
            var collections = new[]
            {
                ("spaces", "sp"), ("folders", "f"), ("policies", "d"),
                ("schemas", "sch"), ("datarefs", "ref"), ("tests", "t"),
            };

            foreach (var (name, idPrefix) in collections)
            {
                if (body[name] is not JsonArray items) continue;

                var existing = await _store.CountAsync(name, ct);
                if (existing > 0 && !force)
                {
                    report[name] = $"skipped ({existing} existing)";
                    continue;
                }
                if (existing > 0) await _store.DeleteWhereAsync(name, "id != ''", ct);

                var inserted = 0;
                foreach (var item in items.ToArray())
                {
                    if (item is not JsonObject obj) continue;
                    await _store.InsertAsync(name, (JsonObject)obj.DeepClone(), idPrefix, ct);
                    inserted++;
                }
                report[name] = $"seeded {inserted}";
            }

            return report;
        }

        // ─── helpers ───────────────────────────────────────────────────────────────

        private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        private static string Slug(string s) =>
            string.Join("-",
                    new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                is { Length: > 0 } slug ? slug : "policy";

        private static JsonNode DefaultContent() => new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = "Describe the policy in plain language, then formalise key sentences as rules. Type @ inside a rule sentence to bind a schema field.",
                }),
            }),
        };
    }
}
