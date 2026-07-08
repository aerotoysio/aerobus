using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Security
{
    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; }
        public PermissionRequirement(string permission) => Permission = permission;
    }

    public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement req)
        {
            // Gather all perm claims once
            var perms = context.User.FindAll("perm").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Exact match
            if (perms.Contains(req.Permission))
            {
                context.Succeed(req);
                return Task.CompletedTask;
            }

            // Global admin override
            if (perms.Contains("admin.all"))
            {
                context.Succeed(req);
                return Task.CompletedTask;
            }

            // Optional: resource admin override, e.g. "company.all" satisfies "company.save"
            var dot = req.Permission.IndexOf('.');
            if (dot > 0)
            {
                var resourceAll = req.Permission[..dot] + ".all";
                if (perms.Contains(resourceAll))
                {
                    context.Succeed(req);
                    return Task.CompletedTask;
                }
            }

            return Task.CompletedTask;
        }
    }

    public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

        public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            // Dynamic policy: any policy name becomes a "perm = policyName" requirement
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
    }
}
