using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Identity
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the Keycloak-backed identity module: options (bound to the
        /// "Keycloak" config section), the Admin REST client (service-account
        /// authenticated, singleton so the token cache is shared) and the
        /// org-scoped <see cref="Services.Identity.IdentityService"/> behind the
        /// /identity endpoints. The Keycloak *authentication scheme* is wired in
        /// AddSecurity — this is the management-plane side.
        /// </summary>
        public static IServiceCollection AddIdentity(this IServiceCollection services, IConfiguration config)
        {
            services.Configure<KeycloakOptions>(config.GetSection(KeycloakOptions.SectionName));
            services.AddMemoryCache();
            services.AddHttpClient(KeycloakAdminClient.HttpClientName);
            services.AddSingleton<KeycloakAdminClient>();
            services.AddSingleton<RbacCacheVersion>();
            services.AddScoped<Repositories.Identity.IOrgRoles, Repositories.Identity.OrgRoles>();
            services.AddScoped<Repositories.Identity.IOrgRoleAssignments, Repositories.Identity.OrgRoleAssignments>();
            services.AddScoped<Repositories.Identity.IUserProfiles, Repositories.Identity.UserProfiles>();
            services.AddScoped<Services.Identity.IdentityService>();
            return services;
        }
    }
}
