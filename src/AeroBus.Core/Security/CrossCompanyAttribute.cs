using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AeroBus.Core.Security
{
    /// <summary>
    /// Marker attribute on an endpoint whose handler legitimately spans
    /// multiple companies (e.g. global admin reports, health checks).
    /// The <c>UseTenantBypassForCrossCompany</c> middleware reads this off
    /// the matched endpoint and toggles <see cref="ITenantContext.BypassTenancy"/>
    /// for the request, so tenant-aware reads may return cross-company data.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class CrossCompanyAttribute : Attribute { }

    public static class CrossCompanyEndpointExtensions
    {
        /// <summary>
        /// Fluent helper: <c>group.MapGet(...).RequireCrossCompany()</c> attaches
        /// <see cref="CrossCompanyAttribute"/> as endpoint metadata. Works for
        /// individual endpoints and route groups.
        /// </summary>
        public static TBuilder RequireCrossCompany<TBuilder>(this TBuilder builder)
            where TBuilder : IEndpointConventionBuilder
        {
            builder.Add(b => b.Metadata.Add(new CrossCompanyAttribute()));
            return builder;
        }

        /// <summary>
        /// Pipeline middleware: when the matched endpoint carries
        /// <see cref="CrossCompanyAttribute"/>, set <see cref="ITenantContext.BypassTenancy"/>.
        /// Must run after <c>UseRouting</c> and before <c>UseAuthorization</c>.
        /// </summary>
        public static IApplicationBuilder UseTenantBypassForCrossCompany(this IApplicationBuilder app) =>
            app.Use(async (ctx, next) =>
            {
                var endpoint = ctx.GetEndpoint();
                if (endpoint?.Metadata.GetMetadata<CrossCompanyAttribute>() is not null)
                {
                    var tenant = ctx.RequestServices.GetService(typeof(ITenantContext)) as ITenantContext;
                    if (tenant is not null) tenant.BypassTenancy = true;
                }
                await next(ctx);
            });
    }
}
