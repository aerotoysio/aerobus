using AeroBus.Core.Services.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Admin
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the control-plane (admin) repositories and services:
        /// companies, company configs, users, roles, permissions, workspaces
        /// and API tokens. Auth wiring lives in <c>AddSecurity</c>.
        /// </summary>
        public static IServiceCollection AddAdmin(this IServiceCollection services)
        {
            services.AddScoped<ICompanies, Companies>();
            services.AddScoped<CompanyService>();

            services.AddScoped<IApiTokens, ApiTokens>();
            services.AddScoped<ApiTokenService>();

            services.AddScoped<ICompanyConfigs, CompanyConfigs>();
            services.AddScoped<CompanyConfigService>();

            services.AddScoped<IPermissions, Permissions>();
            services.AddScoped<PermissionService>();

            services.AddScoped<IRolePermissions, RolePermissions>();
            services.AddScoped<RolePermissionsService>();

            services.AddScoped<IRoles, Roles>();
            services.AddScoped<RoleService>();

            services.AddScoped<IUsers, Users>();
            services.AddScoped<UserService>();

            services.AddScoped<IWorkspaces, Workspaces>();
            services.AddScoped<WorkspaceService>();

            return services;
        }
    }
}
