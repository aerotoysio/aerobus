using AeroBus.Core.Services.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Admin
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the control-plane (admin) repositories and services:
        /// companies, company configs, workspaces and API tokens (the store
        /// behind /identity/agents and the ab_ authentication handler).
        /// Users/roles/permissions management lives in the Keycloak-backed
        /// /identity module — the legacy ooms user stack was removed.
        /// </summary>
        public static IServiceCollection AddAdmin(this IServiceCollection services)
        {
            services.AddScoped<ICompanies, Companies>();
            services.AddScoped<CompanyService>();

            // SaaS control plane: the tenant registry (org → its own database) + the
            // provisioning service that creates and seeds a new org's database.
            services.AddScoped<IOrganisations, Organisations>();
            services.AddScoped<ProvisioningService>();

            services.AddScoped<IApiTokens, ApiTokens>();
            services.AddScoped<ApiTokenService>();

            services.AddScoped<ICompanyConfigs, CompanyConfigs>();
            services.AddScoped<CompanyConfigService>();

            services.AddScoped<IWorkspaces, Workspaces>();
            services.AddScoped<WorkspaceService>();

            return services;
        }
    }
}
