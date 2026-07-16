using System.Text.Json.Nodes;
using AeroBus.Core.Services.PolicyStudio;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.PolicyStudio
{
    /// <summary>
    /// Policy Studio backend — the authoring surface for policy documents that
    /// compile to RuleForge rules. Ported from the standalone RuleForge.Admin dev
    /// service into AeroBus: the routes drop the old <c>/api</c> prefix (the group
    /// mounts at <c>/policy-studio</c>) and are now secured. The group requires
    /// <c>policystudio.view</c>; every write requires <c>policystudio.manage</c>.
    /// The area is platform-level (global content), so no per-company scoping is
    /// applied. Publish compiles the policy and cuts an engine release via the
    /// shared rules-authoring path (see <see cref="PolicyStudioService"/>).
    /// </summary>
    public static class PolicyStudioEndpoints
    {
        private const string Manage = "policystudio.manage";

        public static RouteGroupBuilder PolicyStudioMapping(this RouteGroupBuilder group)
        {
            // ── tree ─────────────────────────────────────────────────────────────
            group.MapGet("/tree", async ([FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetTreeAsync(ct)));

            // ── spaces / folders ─────────────────────────────────────────────────
            group.MapPost("/spaces", async ([FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateSpaceAsync(body, ct))).RequireAuthorization(Manage);

            group.MapPost("/folders", async ([FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateFolderAsync(body, ct))).RequireAuthorization(Manage);

            group.MapPut("/folders/{id}", async (string id, [FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.UpdateFolderAsync(id, body, ct)) is { } f ? Results.Ok(f) : Results.NotFound()).RequireAuthorization(Manage);

            group.MapDelete("/folders/{id}", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                await svc.DeleteFolderAsync(id, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Manage);

            // ── policies ─────────────────────────────────────────────────────────
            group.MapPost("/policies", async ([FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreatePolicyAsync(body, ct))).RequireAuthorization(Manage);

            group.MapGet("/policies/{id}", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.GetPolicyAsync(id, ct)) is { } p ? Results.Ok(p) : Results.NotFound());

            group.MapPut("/policies/{id}", async (string id, [FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.UpdatePolicyAsync(id, body, ct)) is { } p ? Results.Ok(p) : Results.NotFound()).RequireAuthorization(Manage);

            group.MapDelete("/policies/{id}", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                await svc.DeletePolicyAsync(id, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Manage);

            // Publish = freeze the policy version AND cut an engine release. A compile
            // error blocks the publish (422); parse warnings still publish.
            group.MapPost("/policies/{id}/publish", async (
                string id, [FromBody] JsonObject? body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
            {
                try
                {
                    return (await svc.PublishPolicyAsync(id, body, ct)) is { } p ? Results.Ok(p) : Results.NotFound();
                }
                catch (RuleCompiler.CompileException ex)
                {
                    return Results.UnprocessableEntity(new { errors = ex.Errors });
                }
            }).RequireAuthorization(Manage);

            // Compile preview — the rule graph a publish would release, without releasing.
            group.MapGet("/policies/{id}/compiled", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
            {
                try
                {
                    return (await svc.CompilePreviewAsync(id, ct)) is { } preview ? Results.Ok(preview) : Results.NotFound();
                }
                catch (RuleCompiler.CompileException ex)
                {
                    return Results.UnprocessableEntity(new { errors = ex.Errors });
                }
            });

            group.MapGet("/releases", async ([FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListReleasesAsync(ct)));

            // ── schemas ──────────────────────────────────────────────────────────
            group.MapGet("/schemas", async ([FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListSchemasAsync(ct)));

            group.MapPost("/schemas", async ([FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateSchemaAsync(body, ct))).RequireAuthorization(Manage);

            group.MapPut("/schemas/{id}", async (string id, [FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.ReplaceSchemaAsync(id, body, ct)) is { } s ? Results.Ok(s) : Results.NotFound()).RequireAuthorization(Manage);

            group.MapDelete("/schemas/{id}", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                await svc.DeleteSchemaAsync(id, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Manage);

            // ── data references ──────────────────────────────────────────────────
            group.MapGet("/datarefs", async ([FromQuery] string? policyId, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListDataRefsAsync(policyId, ct)));

            group.MapPost("/datarefs", async ([FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateDataRefAsync(body, ct))).RequireAuthorization(Manage);

            group.MapPut("/datarefs/{id}", async (string id, [FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.ReplaceDataRefAsync(id, body, ct)) is { } r ? Results.Ok(r) : Results.NotFound()).RequireAuthorization(Manage);

            group.MapDelete("/datarefs/{id}", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                await svc.DeleteDataRefAsync(id, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Manage);

            // ── tests ────────────────────────────────────────────────────────────
            group.MapGet("/policies/{id}/tests", async (string id, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListTestsAsync(id, ct)));

            group.MapPost("/policies/{id}/tests", async (string id, [FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateTestAsync(id, body, ct))).RequireAuthorization(Manage);

            group.MapPut("/tests/{testId}", async (string testId, [FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.ReplaceTestAsync(testId, body, ct)) is { } t ? Results.Ok(t) : Results.NotFound()).RequireAuthorization(Manage);

            group.MapDelete("/tests/{testId}", async (string testId, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                await svc.DeleteTestAsync(testId, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Manage);

            group.MapPost("/policies/{id}/tests/run", async (
                string id, [FromBody] JsonObject? body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                (await svc.RunTestsAsync(id, body, ct)) is { } results
                    ? Results.Ok(new { results })
                    : Results.NotFound(new { error = $"policy {id} not found" })).RequireAuthorization(Manage);

            // ── settings ─────────────────────────────────────────────────────────
            group.MapGet("/settings", async ([FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetSettingsAsync(ct)));

            group.MapPut("/settings", async ([FromBody] JsonObject body, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.SaveSettingsAsync(body, ct))).RequireAuthorization(Manage);

            // ── seed (dev) ───────────────────────────────────────────────────────
            group.MapPost("/seed", async ([FromBody] JsonObject body, [FromQuery] bool? force, [FromServices] PolicyStudioService svc, CancellationToken ct) =>
                Results.Ok(await svc.SeedAsync(body, force == true, ct))).RequireAuthorization(Manage);

            return group;
        }
    }
}
