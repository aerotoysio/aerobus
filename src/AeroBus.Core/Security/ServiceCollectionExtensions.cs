using System.Text;
using AeroBus.Core.Model.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AeroBus.Core.Security
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Authentication + authorization wiring: JWT-or-ApiKey policy scheme,
        /// permission-claim policies, password hashing, token issuance and
        /// tenant-context plumbing. Jwt settings come from the "Jwt"
        /// configuration section (Issuer, Audience, Key) with a JWT_KEY
        /// environment-variable fallback for the signing key.
        /// </summary>
        public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration config)
        {
            var jwtSection = config.GetSection("Jwt");
            var jwtIssuer = jwtSection["Issuer"];
            var jwtAudience = jwtSection["Audience"];
            var jwtKey = jwtSection["Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
                jwtKey = System.Environment.GetEnvironmentVariable("JWT_KEY");
            if (string.IsNullOrWhiteSpace(jwtKey))
                throw new InvalidOperationException("JWT signing key is not configured — set Jwt:Key or the JWT_KEY environment variable.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

            // The default scheme is a policy scheme that inspects the bearer
            // token format and forwards to JWT or ApiKey accordingly. This lets
            // [Authorize] endpoints accept either credential without per-route
            // configuration.
            const string DefaultScheme = "JwtOrApiKey";

            services
                .AddAuthentication(DefaultScheme)
                .AddPolicyScheme(DefaultScheme, "JWT user-session or ApiKey", options =>
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
                        return JwtBearerDefaults.AuthenticationScheme;
                    };
                })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtAudience,
                        IssuerSigningKey = key,
                        ClockSkew = TimeSpan.FromMinutes(1) // tighten for tests
                    };
                })
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyAuthenticationHandler.SchemeName, _ => { });

            services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddAuthorization(); // keep this too

            services.AddSingleton(new Services.Security.TokenService(jwtIssuer!, jwtAudience!, key));

            // Tenant context plumbing — resolved from the authenticated principal;
            // consumed by tenant-aware reads (see ITenantContext).
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContext, TenantContext>();

            return services;
        }
    }
}
