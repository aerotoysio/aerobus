using AeroBus.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AeroBus.Core.Security
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Authentication + authorization wiring. Two credential types:
        /// Keycloak-issued user tokens (OIDC, the aerostudio path — configured
        /// via the "Keycloak" section) and ab_ API keys (programmatic agents,
        /// managed through /identity/agents). Permission-claim policies and
        /// tenant-context plumbing apply to both. The legacy self-issued HS256
        /// JWT path (ooms /admin/users authenticate) has been removed —
        /// Keycloak is the only interactive identity source.
        /// </summary>
        public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration config)
        {
            var keycloak = new KeycloakOptions();
            config.GetSection(KeycloakOptions.SectionName).Bind(keycloak);

            // The default scheme is a policy scheme that inspects the bearer
            // token format: ab_ keys go to the ApiKey handler, everything else
            // to Keycloak. With Keycloak unconfigured (e.g. a channel-only
            // deployment) non-ab_ bearers fall to the ApiKey handler, which
            // rejects them cleanly with a 401.
            const string DefaultScheme = "KeycloakOrApiKey";

            var auth = services
                .AddAuthentication(DefaultScheme)
                .AddPolicyScheme(DefaultScheme, "Keycloak user-session or ApiKey", options =>
                {
                    options.ForwardDefaultSelector = ctx =>
                    {
                        var raw = ctx.Request.Headers.Authorization.ToString();
                        const string bearerPrefix = "Bearer ";
                        if (raw.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var token = raw.AsSpan(bearerPrefix.Length).Trim();
                            if (token.StartsWith("ab_", StringComparison.Ordinal))
                                return ApiKeyAuthenticationHandler.SchemeName;
                        }
                        return keycloak.Enabled ? KeycloakOptions.Scheme : ApiKeyAuthenticationHandler.SchemeName;
                    };
                })
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyAuthenticationHandler.SchemeName, _ => { });

            if (keycloak.Enabled)
            {
                auth.AddJwtBearer(KeycloakOptions.Scheme, options =>
                {
                    // Signing keys come from the realm's JWKS endpoint via discovery.
                    options.Authority = keycloak.Authority;
                    options.RequireHttpsMetadata = keycloak.Authority.StartsWith("https", StringComparison.OrdinalIgnoreCase);
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = keycloak.Authority,
                        ValidAudience = keycloak.Audience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                    // Realm roles -> perm claims, custom org roles, organization -> companyId.
                    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                    {
                        OnTokenValidated = KeycloakClaimsTransformer.TransformAsync
                    };
                });
            }

            services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddAuthorization(); // keep this too

            // Tenant context plumbing — resolved from the authenticated principal;
            // consumed by tenant-aware reads (see ITenantContext).
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContext, TenantContext>();

            return services;
        }
    }
}
