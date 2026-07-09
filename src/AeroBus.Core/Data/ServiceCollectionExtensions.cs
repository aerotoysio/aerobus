using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Data
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Keyed-service key for the rules-authoring client: always the server's
        /// DEFAULT database (flat routes), regardless of
        /// <see cref="DocumentForgeOptions.Database"/> — RuleForge reads its
        /// rules/reference-set collections from the default database.
        /// </summary>
        public const string RulesClientKey = "documentforge-rules";

        /// <summary>
        /// Registers the DocumentForge client (typed HttpClient) + the typed
        /// <see cref="IDocumentStore"/> over it. Settings bind from the
        /// <c>DocumentForge</c> section.
        /// </summary>
        public static IServiceCollection AddDocumentForge(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DocumentForgeOptions>(configuration.GetSection(DocumentForgeOptions.SectionName));
            services.AddHttpClient<IDocumentForgeClient, DocumentForgeClient>();
            services.AddScoped<IDocumentStore, DocumentStore>();

            services.AddHttpClient(RulesClientKey);
            services.AddKeyedScoped<IDocumentForgeClient>(RulesClientKey, (sp, _) =>
                new DocumentForgeClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(RulesClientKey),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DocumentForgeOptions>>().Value,
                    database: null));

            return services;
        }
    }
}
