using AeroBus.Core.Data;
using AeroBus.Core.Repositories.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace AeroBus.Core.Security
{
    /// <summary>
    /// Resolves the caller's tenant database once per request and stamps it onto
    /// <see cref="ITenantDatabase"/>, so the tenant-routed document client sends the
    /// request's business reads/writes to the org's own DocumentForge database.
    ///
    /// Runs after authentication (needs the <c>companyId</c> claim). The mapping
    /// companyId → org short-name is looked up from the control-plane registry and
    /// cached briefly. When there's no authenticated tenant, or the org isn't
    /// provisioned, nothing is stamped and the client falls back to the statically
    /// configured database — preserving today's single-DB behaviour.
    /// </summary>
    public sealed class TenantDatabaseMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(
            HttpContext context, ITenantContext tenant, ITenantDatabase db, IOrganisations orgs, IMemoryCache cache)
        {
            if (tenant.CallerCompanyId is { } companyId)
            {
                var shortName = await cache.GetOrCreateAsync($"tenantdb:{companyId}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    var org = await orgs.GetByIdAsync(companyId);
                    return string.IsNullOrWhiteSpace(org?.ShortName) ? null : org!.ShortName;
                });

                if (!string.IsNullOrWhiteSpace(shortName))
                    db.CurrentDatabase = shortName;
            }

            await next(context);
        }
    }

    public static class TenantDatabaseMiddlewareExtensions
    {
        public static IApplicationBuilder UseTenantDatabaseRouting(this IApplicationBuilder app) =>
            app.UseMiddleware<TenantDatabaseMiddleware>();
    }
}
