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
