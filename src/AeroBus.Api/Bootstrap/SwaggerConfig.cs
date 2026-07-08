using System.Reflection;
using Microsoft.OpenApi.Models;

namespace AeroBus.Api.Bootstrap
{
    /// <summary>
    /// OpenAPI / Swagger wiring. One "v1" document ("AeroBus API") describes
    /// every endpoint group; the tags applied in <see cref="AppEndpoints"/>
    /// (Health, Admin, Catalogue, Customer Management, Offer, Order, Rules,
    /// Events) become the groupings in the UI. A single Bearer security scheme
    /// carries either a user JWT or an <c>ab_</c> API key, so the "Authorize"
    /// button works for both credential types.
    /// </summary>
    public static class SwaggerConfig
    {
        private const string BearerScheme = "Bearer";

        public static IServiceCollection AddAeroBusSwagger(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "AeroBus API",
                    Version = "v1",
                    Description =
                        "The backbone between distribution channels and the open foundation " +
                        "(DocumentForge for storage, RuleForge for dynamic rules). One API " +
                        "surface: control plane (companies/users/roles/API tokens), catalogue " +
                        "and flight building, offer shopping, order lifecycle, rule filing, and " +
                        "a pub/sub event backbone.\n\n" +
                        "Authenticate with the **Authorize** button using a bearer token — " +
                        "either a user JWT (from `POST /admin/users/{companySlug}/authenticate`) " +
                        "or an `ab_` API key (from `POST /admin/api-tokens`).",
                });

                // A single HTTP bearer scheme covers both credential kinds: the
                // JwtOrApiKey policy scheme routes on the token prefix, so the UI
                // only needs to send `Authorization: Bearer <token>`.
                options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT or ab_ API key",
                    In = ParameterLocation.Header,
                    Description =
                        "Paste a bearer token (the raw JWT or ab_ key — do NOT prefix with " +
                        "\"Bearer\", Swagger adds it). JWTs come from the authenticate endpoint; " +
                        "ab_ keys from /admin/api-tokens.",
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = BearerScheme,
                            },
                        },
                        Array.Empty<string>()
                    },
                });

                // Fold the API XML doc in for endpoint/summary descriptions when present.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
                }

                // Minimal-API groups don't carry a controller name, so give every
                // operation a stable, unique id derived from its route + method to
                // keep the generated document (and any client-gen) deterministic.
                options.CustomOperationIds(api =>
                {
                    var method = api.HttpMethod?.ToLowerInvariant() ?? "get";
                    var path = (api.RelativePath ?? string.Empty)
                        .Replace("{", string.Empty, StringComparison.Ordinal)
                        .Replace("}", string.Empty, StringComparison.Ordinal)
                        .Replace("/", "_", StringComparison.Ordinal)
                        .Replace(":", "_", StringComparison.Ordinal)
                        .Trim('_');
                    return string.IsNullOrEmpty(path) ? method : $"{method}_{path}";
                });
            });

            return services;
        }
    }
}
