using System.Security.Claims;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public static class RolePermissionsEndpoints
    {
        public static RouteGroupBuilder AdminRolePermissionsMapping(this RouteGroupBuilder group)
        {
            group.MapPost("/{roleId}/{permissionId}", async (Guid roleId, Guid permissionId, [FromServices] RolePermissionsService svc, ClaimsPrincipal user) =>
                (await svc.AddRolePermissionAsync(roleId, permissionId)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/{roleId}", async (Guid roleId, [FromServices] RolePermissionsService svc, ClaimsPrincipal user) =>
                (await svc.GetPermissionsForRoleAsync(roleId)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapDelete("/{roleId}/{permissionId}", async (Guid roleId, Guid permissionId, [FromServices] RolePermissionsService svc, ClaimsPrincipal user) =>
            {
                try { _ = await svc.RemoveRolePermissionAsync(roleId, permissionId); return Results.NoContent(); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            return group;
        }
    }
}
