using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Admin;
using AeroBus.Core.Services.Security;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public static class UsersEndpoints
    {
        public static RouteGroupBuilder AdminUsersMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id}", async (Guid id, [FromServices] UserService svc, ClaimsPrincipal user) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound())
                .RequireAuthorization();

            group.MapGet("/company/{companyId}", async (Guid companyId, [FromServices] UserService svc, ClaimsPrincipal user) =>
                (await svc.GetByCompanyAsync(companyId)) is { } x ? Results.Ok(x) : Results.NotFound())
                .RequireAuthorization();

            group.MapPost("/save", async ([FromBody] User m, [FromServices] UserService userService, [FromServices] CompanyService companyService, ClaimsPrincipal user) =>
            {
                try { return Results.Ok(await userService.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            }).RequireAuthorization();

            // Login: anonymous by design — this is where a JWT is minted.
            group.MapPost("/{companySlug}/authenticate", async ([FromBody] User m, [FromRoute] string companySlug, [FromServices] UserService userService, [FromServices] CompanyService companyService, [FromServices] TokenService tokens) =>
            {
                var user = await userService.AuthenticateAsync(m.Email, m.Password ?? string.Empty, companySlug);

                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var permlist = await userService.GetPermissionsByUserIdAsync(user.Id);

                var claims = new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, user.Email),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Role, user.RoleId.ToString() ?? string.Empty),
                    new Claim("companyId", user.CompanyId.ToString() ?? string.Empty),
                    new Claim("workingId", Guid.Empty.ToString())
                };

                var permissions = new List<string>();
                foreach (var perm in permlist)
                {
                    claims = claims.Append(new Claim("perm", perm.Code)).ToArray();
                    permissions.Add(perm.Code);
                }

                var accessToken = tokens.CreateAccessToken(claims, TimeSpan.FromDays(365));
                // Optional: also return a refresh token (opaque random string) from your DB.
                return Results.Ok(new { user, accessToken, permissions });
            }).AllowAnonymous();

            group.MapPost("/{companySlug}/WorkingId/{workingId:Guid}", async ([FromRoute] string companySlug, [FromRoute] Guid workingId, [FromServices] UserService userService, [FromServices] TokenService tokens, [FromServices] CompanyService companyService, ClaimsPrincipal user) =>
            {
                var userid = user.GetUserId();

                var userProfile = await userService.GetByIdAsync(userid);

                if (userProfile == null)
                {
                    return Results.Unauthorized();
                }

                var permlist = await userService.GetPermissionsByUserIdAsync(userProfile.Id);

                var claims = new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, userProfile.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, userProfile.Email),
                    new Claim(ClaimTypes.Name, userProfile.Name),
                    new Claim(ClaimTypes.Role, userProfile.RoleId.ToString() ?? string.Empty),
                    new Claim("companyId", userProfile.CompanyId.ToString() ?? string.Empty),
                    new Claim("workingId", workingId.ToString())
                };

                var permissions = new List<string>();
                foreach (var perm in permlist)
                {
                    claims = claims.Append(new Claim("perm", perm.Code)).ToArray();
                    permissions.Add(perm.Code);
                }

                var accessToken = tokens.CreateAccessToken(claims, TimeSpan.FromDays(365));
                // Optional: also return a refresh token (opaque random string) from your DB.
                return Results.Ok(new { user, accessToken, permissions });
            }).RequireAuthorization();

            group.MapDelete("/{id}", async (Guid id, [FromServices] UserService svc) =>
            {
                try { _ = await svc.DeleteAsync(id); return Results.NoContent(); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            }).RequireAuthorization();

            return group;
        }
    }
}
