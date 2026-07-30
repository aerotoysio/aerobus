using System.Text.Json;
using System.Text.Json.Nodes;
using AeroBus.Core.Services.Rules;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Rules
{
    /// <summary>
    /// Rules authoring proxy — one API surface over RuleForge's DocumentForge
    /// collections. Thin JSON passthroughs (rule docs use string ids), plus the
    /// publish flow that snapshots a version, binds the environment, and refreshes
    /// RuleForge. events: rule.published via outbox in Phase 6.
    /// </summary>
    public static class RulesEndpoints
    {
        public static RouteGroupBuilder RulesMapping(this RouteGroupBuilder group)
        {
            // ── rules ──────────────────────────────────────────────────────────
            group.MapGet("/", async ([FromQuery] string? status, [FromQuery] string? category, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListRulesAsync(status, category, ct)));

            group.MapGet("/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                (await svc.GetRuleAsync(id, ct)) is { } r ? Results.Ok(r) : Results.NotFound());

            group.MapPut("/{id}", async (string id, [FromBody] JsonElement body, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
            {
                try
                {
                    var node = JsonNode.Parse(body.GetRawText())!;
                    var saved = await svc.UpsertRuleAsync(id, node, ct);
                    return Results.Ok(JsonNode.Parse(saved.ToJsonString()));
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            group.MapDelete("/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                await svc.DeleteRuleAsync(id, ct) ? Results.NoContent() : Results.NotFound());

            // Test console: evaluate the current DRAFT in-process against a
            // request payload. Debug defaults ON — the console exists to show
            // the trace. Never touches published bindings or live traffic.
            group.MapPost("/{id}/test", async (
                string id,
                [FromBody] JsonElement request,
                [FromQuery] bool? debug,
                [FromServices] RuleAuthoringService svc,
                [FromServices] AeroBus.Core.Rules.EmbeddedRuleForgeClient embedded,
                CancellationToken ct) =>
            {
                var rule = await svc.GetRuleAsync(id, ct);
                if (rule is null) return Results.NotFound();
                try
                {
                    // Composites compile before the engine sees the draft.
                    var expanded = await svc.ExpandCompositesAsync(
                        JsonNode.Parse(rule.Value.GetRawText())!.AsObject(), ct);
                    var ruleJson = JsonSerializer.Deserialize<JsonElement>(expanded.ToJsonString());
                    var envelope = await embedded.EvaluateDraftAsync(ruleJson, request, debug ?? true, ct);
                    return Results.Ok(envelope);
                }
                catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            group.MapPost("/{id}/publish", async (
                string id, [FromQuery] string? env, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
            {
                try
                {
                    var result = await svc.PublishRuleAsync(id, string.IsNullOrWhiteSpace(env) ? "dev" : env, ct);
                    return Results.Ok(new { ruleId = result.RuleId, version = result.Version, env = result.Env, refreshed = result.RuleForgeRefreshed });
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            // ── input shapes ───────────────────────────────────────────────────
            group.MapGet("/shapes", async ([FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListShapesAsync(ct)));

            group.MapGet("/shapes/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                (await svc.GetShapeAsync(id, ct)) is { } s ? Results.Ok(s) : Results.NotFound());

            group.MapPut("/shapes/{id}", async (string id, [FromBody] JsonElement body, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
            {
                try
                {
                    var node = JsonNode.Parse(body.GetRawText())!;
                    var saved = await svc.UpsertShapeAsync(id, node, ct);
                    return Results.Ok(JsonNode.Parse(saved.ToJsonString()));
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            group.MapDelete("/shapes/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                await svc.DeleteShapeAsync(id, ct) ? Results.NoContent() : Results.NotFound());

            // ── node templates (specialised nodes) ─────────────────────────────
            group.MapGet("/node-templates", async ([FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListNodeTemplatesAsync(ct)));

            group.MapGet("/node-templates/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                (await svc.GetNodeTemplateAsync(id, ct)) is { } t ? Results.Ok(t) : Results.NotFound());

            group.MapPut("/node-templates/{id}", async (string id, [FromBody] JsonElement body, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
            {
                try
                {
                    var node = JsonNode.Parse(body.GetRawText())!;
                    var saved = await svc.UpsertNodeTemplateAsync(id, node, ct);
                    return Results.Ok(JsonNode.Parse(saved.ToJsonString()));
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            group.MapDelete("/node-templates/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                await svc.DeleteNodeTemplateAsync(id, ct) ? Results.NoContent() : Results.NotFound());

            // ── reference sets ─────────────────────────────────────────────────
            group.MapGet("/reference-sets/{id}", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                (await svc.GetReferenceSetAsync(id, ct)) is { } r ? Results.Ok(r) : Results.NotFound());

            group.MapPut("/reference-sets/{id}", async (string id, [FromBody] JsonElement body, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
            {
                try
                {
                    var node = JsonNode.Parse(body.GetRawText())!;
                    var saved = await svc.UpsertReferenceSetAsync(id, node, ct);
                    return Results.Ok(JsonNode.Parse(saved.ToJsonString()));
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            group.MapPost("/reference-sets/{id}/publish", async (string id, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
            {
                try
                {
                    var result = await svc.PublishReferenceSetAsync(id, ct);
                    return Results.Ok(new { referenceSetId = result.RuleId, version = result.Version, refreshed = result.RuleForgeRefreshed });
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            // ── environments ───────────────────────────────────────────────────
            group.MapGet("/environments/{name}", async (string name, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                (await svc.GetEnvironmentAsync(name, ct)) is { } e ? Results.Ok(e) : Results.NotFound());

            return group;
        }
    }
}
