using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Identity
{
    /// <summary>
    /// Bridges Keycloak access tokens into the aerobus principal model on token
    /// validation: realm roles become <c>perm</c> claims (so the existing
    /// <see cref="Security.PermissionHandler"/> policies work unchanged), and the
    /// caller's organisation is resolved to its Keycloak id and surfaced as the
    /// <c>companyId</c> claim used by the tenant context.
    /// </summary>
    public static class KeycloakClaimsTransformer
    {
        /// <summary>
        /// System-role grants over the PermissionCatalog. The IdP stays coarse
        /// (four fixed realm roles); tenant-specific capability comes from custom
        /// org roles, which are expanded on top of these. aerostudio gates its UI
        /// from /identity/me, so this map is the single source of truth.
        /// </summary>
        private static readonly Dictionary<string, string[]> RolePermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["platform-admin"] = ["admin.all"],
            ["org-admin"] =
            [
                "dashboard.view", "org.all", "identity.all", "role.all", "agent.all",
                "offers.all", "ibe.all", "ancillary.all", "orders.all",
                "customers.all", "catalogue.all", "rules.all", "events.all",
            ],
            ["editor"] =
            [
                "dashboard.view", "offers.all", "ibe.all", "ancillary.all",
                "catalogue.view", "orders.view", "customers.view", "rules.view",
            ],
            ["viewer"] =
            [
                "dashboard.view", "offers.view", "ibe.view", "ancillary.view",
                "catalogue.view", "orders.view", "customers.view",
            ],
        };

        public static async Task TransformAsync(TokenValidatedContext ctx)
        {
            if (ctx.Principal is not { } principal) return;

            var extra = new ClaimsIdentity();

            foreach (var role in ReadRealmRoles(principal))
            {
                extra.AddClaim(new Claim(ClaimTypes.Role, role));
                if (RolePermissions.TryGetValue(role, out var perms))
                    foreach (var perm in perms)
                        extra.AddClaim(new Claim("perm", perm));
            }

            // Tenant: the "organization" claim carries the org alias(es); resolve the
            // first one to its Keycloak id (a GUID) so tenant-aware reads keyed on
            // companyId work for Keycloak principals too. Cached; a Keycloak admin-API
            // hiccup must not fail authentication.
            var services = ctx.HttpContext.RequestServices;
            var orgAlias = principal.FindFirst("organization")?.Value;
            string? companyId = null;
            if (orgAlias is not null)
            {
                try
                {
                    var cache = services.GetRequiredService<IMemoryCache>();
                    companyId = await cache.GetOrCreateAsync($"kc-org-id:{orgAlias}", async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                        var kc = services.GetRequiredService<KeycloakAdminClient>();
                        return (await kc.GetOrganizationByAliasAsync(orgAlias))?.Id;
                    });
                    if (companyId is not null)
                        extra.AddClaim(new Claim("companyId", companyId));
                }
                catch (Exception ex)
                {
                    Log(services, ex, $"Could not resolve organization '{orgAlias}' to a companyId");
                }
            }

            // Custom org roles: expand the user's assigned roles (aerobus-owned,
            // DocumentForge-backed) into additional perm claims. Cached under the
            // RBAC version so role/assignment edits take effect immediately.
            var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
            if (companyId is not null && Guid.TryParse(sub, out var userId) && Guid.TryParse(companyId, out var orgId))
            {
                try
                {
                    var cache = services.GetRequiredService<IMemoryCache>();
                    var version = services.GetRequiredService<RbacCacheVersion>().Current;
                    var custom = await cache.GetOrCreateAsync($"rbac:{version}:{userId}", async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                        var assignments = services.GetRequiredService<Repositories.Identity.IOrgRoleAssignments>();
                        var roles = services.GetRequiredService<Repositories.Identity.IOrgRoles>();
                        var assignment = await assignments.GetByUserAsync(userId);
                        if (assignment is null || assignment.CompanyId != orgId) return Array.Empty<string>();

                        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var roleId in assignment.RoleIds)
                        {
                            var role = await roles.GetByIdAsync(roleId);
                            if (role is not null && role.CompanyId == orgId)
                                perms.UnionWith(role.Permissions);
                        }
                        return perms.ToArray();
                    });
                    foreach (var perm in custom ?? [])
                        extra.AddClaim(new Claim("perm", perm));
                }
                catch (Exception ex)
                {
                    Log(services, ex, $"Could not expand custom roles for user {userId}");
                }
            }

            principal.AddIdentity(extra);
        }

        private static void Log(IServiceProvider services, Exception ex, string message) =>
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(KeycloakClaimsTransformer))
                .LogWarning(ex, "{Message}", message);

        private static IEnumerable<string> ReadRealmRoles(ClaimsPrincipal principal)
        {
            // The realm_access claim survives inbound mapping as a JSON blob:
            // {"roles":["org-admin","editor",...]}
            var realmAccess = principal.FindFirst("realm_access")?.Value;
            if (realmAccess is null) yield break;

            using var doc = JsonDocument.Parse(realmAccess);
            if (!doc.RootElement.TryGetProperty("roles", out var roles)) yield break;
            foreach (var role in roles.EnumerateArray())
                if (role.GetString() is { } name)
                    yield return name;
        }
    }
}
