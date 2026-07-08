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
            group.MapGet("/", async ([FromQuery] string? status, [FromServices] RuleAuthoringService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListRulesAsync(status, ct)));

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
