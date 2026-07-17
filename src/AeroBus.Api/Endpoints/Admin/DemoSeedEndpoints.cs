using AeroBus.Core.Security;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AeroBus.Api.Endpoints.Admin
{
    /// <summary>
    /// The demo-airline seed behind the onboarding "welcome" experience: the
    /// manifest (what will be / has been loaded) and per-section execution, so a
    /// client can run the sections in order and narrate live progress. Org-scoped
    /// (the caller's own organisation) and gated <c>org.manage</c> — org admins
    /// only. Sections are idempotent, safe to retry.
    /// </summary>
    public static class DemoSeedEndpoints
    {
        public static RouteGroupBuilder DemoSeedMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/", async ([FromServices] DemoSeedService svc, ClaimsPrincipal user, CancellationToken ct) =>
                Results.Ok(await svc.GetManifestAsync(user.GetCompanyId(), ct)));

            group.MapPost("/{section}", async (
                string section, [FromServices] DemoSeedService svc, ClaimsPrincipal user, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await svc.SeedSectionAsync(user.GetCompanyId(), section, ct));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
                catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            });

            return group;
        }
    }
}
