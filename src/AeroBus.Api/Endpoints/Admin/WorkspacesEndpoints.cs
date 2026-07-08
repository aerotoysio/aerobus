using System.Security.Claims;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    /// <summary>
    /// Workspace endpoints. Route shapes match the ooms admin-service so the
    /// existing admin UI can repoint unchanged. The Git-backed actions
    /// (history, promote, ensure-branch, list files) return 501 — the GitHub
    /// integration was deliberately dropped in the AeroBus port.
    /// </summary>
    public static class WorkspacesEndpoints
    {
        public static RouteGroupBuilder AdminWorkspacesMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async ([FromRoute] Guid id, [FromServices] WorkspaceService svc, ClaimsPrincipal user)
                => (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/company/{companyId:guid}", async ([FromRoute] Guid companyId, [FromServices] WorkspaceService svc, ClaimsPrincipal user)
                => Results.Ok(await svc.GetByCompanyAsync(companyId)));

            // GET /admin/workspaces/company/{companyId}/history
            // Git-backed branch timeline — not available without GitHub.
            group.MapGet("/company/{companyId:guid}/history", ([FromRoute] Guid companyId)
                => Results.StatusCode(StatusCodes.Status501NotImplemented));

            group.MapPost("/save", async ([FromBody] Workspace m, [FromServices] WorkspaceService svc, ClaimsPrincipal user) =>
            {
                m.CreatedBy = user.GetUserId();
                try { return Results.Ok(await svc.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            // ---------- Delete with optional cleanup policy ----------
            // ?cleanup=keep|delete|tagAndDelete — accepted for wire
            // compatibility; ignored (no Git branches to clean up).
            group.MapDelete("/{id:guid}", async ([FromRoute] Guid id, [FromQuery] string? cleanup, [FromServices] WorkspaceService svc, ClaimsPrincipal user, [FromQuery] Guid? concurrencyId = null) =>
            {
                try
                {
                    WorkspaceService.WorkspaceBranchCleanup? policy = cleanup?.ToLowerInvariant() switch
                    {
                        "keep" => WorkspaceService.WorkspaceBranchCleanup.Keep,
                        "delete" => WorkspaceService.WorkspaceBranchCleanup.Delete,
                        "taganddelete" => WorkspaceService.WorkspaceBranchCleanup.TagAndDelete,
                        _ => null
                    };

                    var ok = await svc.DeleteAsync(id, concurrencyId ?? Guid.Empty, default, policy);
                    return ok ? Results.NoContent() : Results.NotFound();
                }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            // ---------- Promote to production (was: PR + merge) ----------
            group.MapPost("/{id:guid}/promote", ([FromRoute] Guid id, [FromBody] PromoteRequest body)
                => Results.StatusCode(StatusCodes.Status501NotImplemented));

            // ---------- Ensure branch exists ----------
            group.MapPost("/{id:guid}/ensure-branch", ([FromRoute] Guid id)
                => Results.StatusCode(StatusCodes.Status501NotImplemented));

            // ---------- List files in a workspace branch folder ----------
            // GET /{id}/list?folder=rules
            group.MapGet("/{id:guid}/list", ([FromRoute] Guid id, [FromQuery] string? folder)
                => Results.StatusCode(StatusCodes.Status501NotImplemented));

            return group;
        }

        // ---------- request DTOs ----------

        public sealed class PromoteRequest
        {
            public string? Name { get; set; }
            public string? Description { get; set; }

            // "squash" | "merge" | "rebase"
            public string? MergeMethod { get; set; }
        }
    }
}
