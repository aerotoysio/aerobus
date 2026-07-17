using System.Security.Claims;

namespace AeroBus.Core.Security
{
    public static class UserExtensions
    {
        public static Guid GetCompanyId(this ClaimsPrincipal user)
        {
            var val = user.FindFirstValue("companyId");

            Guid.TryParse(val, out var id);

            return id;
        }

        /// <summary>
        /// Company id to persist on a posted document: the document's own id
        /// when present, otherwise the caller's companyId claim. Guards against
        /// saving documents with CompanyId = Guid.Empty, which no tenant-scoped
        /// query can see.
        /// </summary>
        public static Guid ResolveCompanyId(this ClaimsPrincipal user, Guid? documentCompanyId)
        {
            return documentCompanyId is { } id && id != Guid.Empty
                ? id
                : user.GetCompanyId();
        }

        public static Guid GetWorkspaceId(this ClaimsPrincipal user)
        {
            var val = user.FindFirstValue("workspaceId");

            Guid.TryParse(val, out var id);

            return id;
        }

        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");

            return new Guid(userId!);
        }

        public static string? GetEmail(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("upn");
        }

        public static bool IsAdmin(this ClaimsPrincipal user)
        {
            return user.HasClaim("perm", "admin.all");
        }
    }
}
