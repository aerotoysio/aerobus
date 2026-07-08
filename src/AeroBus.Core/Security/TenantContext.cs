using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AeroBus.Core.Security
{
    /// <summary>
    /// Per-request view of the calling principal's tenant. Resolved from the
    /// authenticated <see cref="ClaimsPrincipal"/> on <see cref="HttpContext.User"/>;
    /// consumed by services and repositories that filter documents by
    /// <c>CompanyId</c>, so individual endpoints don't have to remember to
    /// thread the caller's company through every call.
    /// </summary>
    public interface ITenantContext
    {
        /// <summary>The caller's <c>companyId</c> claim, or null for unauthenticated paths.</summary>
        Guid? CallerCompanyId { get; }

        /// <summary>True when an explicit opt-out has been registered for this
        /// request — see <see cref="CrossCompanyAttribute"/> and the middleware
        /// that honours it. When true, tenant-aware reads may return
        /// cross-company data (e.g. global admin reports, health checks).</summary>
        bool BypassTenancy { get; set; }

        /// <summary>True when the principal authenticated via API key
        /// (<c>apitoken</c> claim is present). Useful for fine-grained policy
        /// decisions.</summary>
        bool IsApiToken { get; }
    }

    public sealed class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _accessor;

        public TenantContext(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public Guid? CallerCompanyId
        {
            get
            {
                var principal = _accessor.HttpContext?.User;
                if (principal is null || principal.Identity?.IsAuthenticated != true) return null;
                var raw = principal.FindFirstValue("companyId");
                return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
            }
        }

        public bool BypassTenancy { get; set; }

        public bool IsApiToken =>
            _accessor.HttpContext?.User?.FindFirstValue("apitoken") == "1";
    }
}
