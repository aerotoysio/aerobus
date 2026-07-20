using AeroBus.Core.Data;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Security
{
    /// <summary>
    /// Resolves the caller's tenant database once per request and stamps it onto
    /// <see cref="ITenantDatabase"/>, so the tenant-routed document client sends the
    /// request's business reads/writes to the org's own DocumentForge database.
    ///
    /// Two resolution paths, in trust order:
    /// <list type="number">
    /// <item><b>The token</b> (authoritative). Runs after authentication; the
    /// <c>companyId</c> claim maps to the org's short name via the control-plane
    /// registry (cached briefly). A Host header can never override this — it is
    /// attacker-controlled; when both are present and disagree, the request is
    /// rejected outright.</item>
    /// <item><b>The subdomain</b> (anonymous surfaces only). When no authenticated
    /// tenant is on the request and a tenancy base domain is configured
    /// (platform config <c>tenancy.baseDomain</c>, appsettings <c>Tenancy</c>
    /// bootstrap), a Host of <c>&lt;slug&gt;.&lt;baseDomain&gt;</c> resolves the
    /// org whose short name is that slug — slug, subdomain and database name are
    /// the same word. This gives public surfaces (IBE, booking, the login page)
    /// their airline context before anyone signs in.</item>
    /// </list>
    ///
    /// When neither resolves, nothing is stamped and the client falls back to the
    /// statically configured database — preserving single-DB dev behaviour.
    /// </summary>
    public sealed class TenantDatabaseMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenant,
            ITenantDatabase db,
            IOrganisations orgs,
            IMemoryCache cache,
            PlatformConfigService platformConfig,
            IOptions<TenancyOptions> tenancyBootstrap,
            ILogger<TenantDatabaseMiddleware> log)
        {
            var hostSlug = await ResolveHostSlugAsync(context, platformConfig, tenancyBootstrap.Value);

            if (tenant.CallerCompanyId is { } companyId)
            {
                var shortName = await cache.GetOrCreateAsync($"tenantdb:{companyId}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    var org = await orgs.GetByIdAsync(companyId);
                    return string.IsNullOrWhiteSpace(org?.ShortName) ? null : org!.ShortName;
                });

                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    // The token is the authority. A Host that names a DIFFERENT
                    // org is either a misconfigured client or someone probing —
                    // refuse rather than quietly serving the token's org under
                    // another airline's subdomain.
                    if (hostSlug is not null && !string.Equals(hostSlug, shortName, StringComparison.OrdinalIgnoreCase))
                    {
                        log.LogWarning(
                            "Tenant mismatch: token org '{TokenOrg}' called via subdomain '{HostSlug}'; rejecting.",
                            shortName, hostSlug);
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new { error = "The signed-in organisation does not match this subdomain." });
                        return;
                    }

                    db.CurrentDatabase = shortName;
                }
            }
            else if (hostSlug is not null)
            {
                // Anonymous + a tenant subdomain: give the request the airline's
                // context (public IBE/booking surfaces). Unknown slugs stamp
                // nothing — the request proceeds on the static fallback.
                var shortName = await cache.GetOrCreateAsync($"tenantdb:host:{hostSlug}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    var org = await orgs.GetByShortNameAsync(hostSlug);
                    return string.Equals(org?.Status, "Active", StringComparison.OrdinalIgnoreCase) ? org!.ShortName : null;
                });

                if (!string.IsNullOrWhiteSpace(shortName))
                    db.CurrentDatabase = shortName;
            }

            await next(context);
        }

        /// <summary>
        /// The tenant slug named by the request's Host, or null when Host-based
        /// tenancy is off (no base domain), the host IS the base domain, or the
        /// shape doesn't match <c>&lt;slug&gt;.&lt;baseDomain&gt;</c>. "www" is
        /// never a tenant.
        /// </summary>
        private static async Task<string?> ResolveHostSlugAsync(
            HttpContext context, PlatformConfigService platformConfig, TenancyOptions bootstrap)
        {
            string? baseDomain;
            try
            {
                baseDomain = await platformConfig.GetOrDefaultAsync("tenancy.baseDomain", bootstrap.BaseDomain ?? string.Empty);
            }
            catch
            {
                // Control store unreachable — auth-time reads will fail loudly
                // elsewhere; tenancy just degrades to token-only resolution.
                baseDomain = bootstrap.BaseDomain;
            }
            if (string.IsNullOrWhiteSpace(baseDomain)) return null;

            var host = context.Request.Host.Host; // no port
            if (string.IsNullOrWhiteSpace(host)) return null;

            baseDomain = baseDomain.Trim().TrimStart('.');
            if (!host.EndsWith("." + baseDomain, StringComparison.OrdinalIgnoreCase)) return null;

            var slug = host[..^(baseDomain.Length + 1)];
            if (slug.Length == 0 || slug.Contains('.')) return null; // deeper subdomains aren't tenants
            if (string.Equals(slug, "www", StringComparison.OrdinalIgnoreCase)) return null;
            return slug.ToLowerInvariant();
        }
    }

    public static class TenantDatabaseMiddlewareExtensions
    {
        public static IApplicationBuilder UseTenantDatabaseRouting(this IApplicationBuilder app) =>
            app.UseMiddleware<TenantDatabaseMiddleware>();
    }
}
