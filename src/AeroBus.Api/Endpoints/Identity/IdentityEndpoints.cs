using System.Security.Claims;
using AeroBus.Core.Identity;
using AeroBus.Core.Model.Identity;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Identity
{
    /// <summary>
    /// Keycloak-backed identity management — the surface aerostudio's
    /// "Users &amp; Roles" pages talk to. Org scoping is enforced in
    /// <see cref="IdentityService"/>; permissions here follow the perm-claim
    /// convention (org-admins hold identity.all via the claims transformer).
    /// </summary>
    public static class IdentityEndpoints
    {
        public static RouteGroupBuilder IdentityMapping(this RouteGroupBuilder group)
        {
            // Who am I + what may I do — aerostudio gates its UI from this, so the
            // permission truth lives server-side only. Any authenticated principal.
            group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
            {
                sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
                email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
                name = user.Identity?.Name ?? user.FindFirstValue("name"),
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(),
                organizations = user.FindAll("organization").Select(c => c.Value),
                permissions = user.FindAll("perm").Select(c => c.Value).Distinct(),
            }));

            // Self-service profile — any authenticated interactive user, no policy.
            group.MapGet("/profile", (
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.GetProfileAsync(user, ct))));

            group.MapPut("/profile", (
                [FromBody] UpdateProfileRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.UpdateProfileAsync(user, req, ct);
                    return Results.NoContent();
                }));

            group.MapPut("/profile/password", (
                [FromBody] ChangePasswordRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.ChangeOwnPasswordAsync(user, req.Password, ct);
                    return Results.NoContent();
                }));

            group.MapPut("/profile/picture", (
                [FromBody] SetPictureRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.SetPictureAsync(user, req.Picture, ct);
                    return Results.NoContent();
                }));

            // Organisation profile + site settings (stored on the Keycloak org).
            group.MapGet("/organization", (
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.GetOrganizationSettingsAsync(user, ct))))
                .RequireAuthorization("org.view");

            group.MapPut("/organization", (
                [FromBody] UpdateOrganizationRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.UpdateOrganizationSettingsAsync(user, req, ct))))
                .RequireAuthorization("org.manage");

            // The assignable permission catalog, grouped for role-builder UIs.
            group.MapGet("/permissions", () => Results.Ok(
                PermissionCatalog.All
                    .GroupBy(p => p.Resource)
                    .Select(g => new
                    {
                        resource = g.Key,
                        permissions = g.Select(p => new { code = p.Code, description = p.Description }),
                    })))
                .RequireAuthorization("role.view");

            group.MapGet("/users", (
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.ListUsersAsync(user, org, ct))))
                .RequireAuthorization("identity.view");

            group.MapPost("/users", (
                [FromBody] CreateUserRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Created($"/identity/users", await svc.CreateUserAsync(user, req, ct))))
                .RequireAuthorization("identity.manage");

            group.MapPut("/users/{id}/roles", (
                string id,
                [FromBody] SetRolesRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.SetRolesAsync(user, id, req, org, ct))))
                .RequireAuthorization("identity.manage");

            group.MapPut("/users/{id}/enabled", (
                string id,
                [FromBody] SetEnabledRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.SetEnabledAsync(user, id, req.Enabled, org, ct);
                    return Results.NoContent();
                }))
                .RequireAuthorization("identity.manage");

            group.MapPost("/users/{id}/reset-password", (
                string id,
                [FromBody] ResetPasswordRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.ResetPasswordAsync(user, id, req.Password, org, ct);
                    return Results.NoContent();
                }))
                .RequireAuthorization("identity.manage");

            group.MapGet("/roles", (
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.ListRolesAsync(user, ct))))
                .RequireAuthorization("identity.view");

            // Custom org roles: tenant-defined permission bundles.
            group.MapPost("/roles", (
                [FromBody] SaveOrgRoleRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Created("/identity/roles", await svc.CreateOrgRoleAsync(user, req, ct))))
                .RequireAuthorization("role.manage");

            group.MapPut("/roles/{id:guid}", (
                Guid id,
                [FromBody] SaveOrgRoleRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.UpdateOrgRoleAsync(user, id, req, ct))))
                .RequireAuthorization("role.manage");

            group.MapDelete("/roles/{id:guid}", (
                Guid id,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.DeleteOrgRoleAsync(user, id, ct);
                    return Results.NoContent();
                }))
                .RequireAuthorization("role.manage");

            // Agents: programmatic accounts (ab_ API keys) scoped to the org.
            group.MapGet("/agents", (
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.ListAgentsAsync(user, org, ct))))
                .RequireAuthorization("agent.view");

            group.MapPost("/agents", (
                [FromBody] CreateAgentRequest req,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () => Results.Created("/identity/agents", await svc.CreateAgentAsync(user, req, org, ct))))
                .RequireAuthorization("agent.manage");

            group.MapDelete("/agents/{id:guid}", (
                Guid id,
                [FromServices] IdentityService svc,
                ClaimsPrincipal user,
                [FromQuery] string? org,
                CancellationToken ct) =>
                Run(async () =>
                {
                    await svc.RevokeAgentAsync(user, id, org, ct);
                    return Results.NoContent();
                }))
                .RequireAuthorization("agent.manage");

            // Platform staff only — the cross-tenant view.
            group.MapGet("/organizations", (
                [FromServices] IdentityService svc,
                CancellationToken ct) =>
                Run(async () => Results.Ok(await svc.ListOrganizationsAsync(ct))))
                .RequireAuthorization("admin.all");

            return group;
        }

        /// <summary>Anonymous tenant self-onboarding (login-page flow). Mounted without authorization.</summary>
        public static Task<IResult> OnboardAsync(
            [FromBody] OnboardRequest req,
            [FromServices] IdentityService svc,
            CancellationToken ct) =>
            Run(async () => Results.Created("/identity/onboarding", await svc.OnboardAsync(req, ct)));

        /// <summary>Maps service failures onto HTTP results so handlers stay expression-bodied.</summary>
        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try
            {
                return await action();
            }
            catch (IdentityException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.Status);
            }
            catch (KeycloakApiException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        }
    }
}
