using System.Security.Claims;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public static class PermissionsEndpoints
    {
        public static RouteGroupBuilder AdminPermissionsMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id}", async (Guid id, [FromServices] PermissionService svc, ClaimsPrincipal user) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/company/{companyId}", async (Guid companyId, [FromServices] PermissionService svc, ClaimsPrincipal user) =>
                (await svc.GetByCompanyAsync(companyId)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapPost("/save", async ([FromBody] Permission m, [FromServices] PermissionService svc, ClaimsPrincipal user) =>
            {
                try { return Results.Ok(await svc.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            group.MapDelete("/{id}", async (Guid id, [FromServices] PermissionService svc, ClaimsPrincipal user, [FromQuery] Guid? concurrencyId = null) =>
            {
                try { _ = await svc.DeleteAsync(id, concurrencyId ?? Guid.Empty); return Results.NoContent(); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            return group;
        }
    }
}
