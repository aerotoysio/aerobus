using System.Security.Claims;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public static class RolesEndpoints
    {
        public static RouteGroupBuilder AdminRolesMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (Guid id, [FromServices] RoleService svc, ClaimsPrincipal user) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/company/{companyId}", async (Guid companyId, [FromServices] RoleService svc, ClaimsPrincipal user)
                => Results.Ok(await svc.GetByCompanyAsync(companyId)));

            group.MapPost("/save", async ([FromBody] Role m, [FromServices] RoleService svc, ClaimsPrincipal user) =>
            {
                try { return Results.Ok(await svc.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            group.MapDelete("/{id:guid}", async (Guid id, [FromServices] RoleService svc, ClaimsPrincipal user, [FromQuery] Guid? concurrencyId = null) =>
            {
                try { _ = await svc.DeleteAsync(id, concurrencyId ?? Guid.Empty); return Results.NoContent(); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            return group;
        }
    }
}
