using System.Security.Claims;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public sealed record PlatformConfigSetRequest(string? Value, bool IsSecret = false, string? Description = null);

    /// <summary>
    /// Platform settings (control database, admin.platformconfig) — the runtime
    /// home of everything that is NOT the Keycloak/DocumentForge bootstrap.
    /// Platform staff only. Secrets are write-only: the list shows a mask and a
    /// PUT replaces the value; plaintext never leaves the server.
    /// </summary>
    public static class PlatformConfigEndpoints
    {
        public static RouteGroupBuilder AdminPlatformConfigMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/", async ([FromServices] PlatformConfigService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(ct)));

            group.MapPut("/{key}", async (
                string key,
                [FromBody] PlatformConfigSetRequest body,
                [FromServices] PlatformConfigService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(key))
                    return Results.BadRequest(new { error = "A non-empty key is required." });
                var view = await svc.SetAsync(key, body.Value, body.IsSecret, body.Description, user.Identity?.Name, ct);
                return Results.Ok(view);
            });

            group.MapDelete("/{key}", async (
                string key,
                [FromServices] PlatformConfigService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                await svc.DeleteAsync(key, user.Identity?.Name, ct) ? Results.NoContent() : Results.NotFound());

            return group;
        }
    }
}
