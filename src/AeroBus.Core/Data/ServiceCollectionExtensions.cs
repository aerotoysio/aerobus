using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Data
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>Named HTTP client for the main (tenant-routed) document client.</summary>
        public const string MainClientName = "documentforge-main";

        /// <summary>
        /// Keyed-service key for the rules-authoring client: always the server's
        /// DEFAULT database (flat routes) — RuleForge reads its rules/reference-set
        /// collections from the default database.
        /// </summary>
        public const string RulesClientKey = "documentforge-rules";

        /// <summary>
        /// Keyed-service key for the SHARED control-plane client + store: a fixed
        /// NAMED database (<see cref="DocumentForgeOptions.ControlDatabase"/>,
        /// default <c>control</c>, ensured at startup). Home of the tenant registry
        /// (admin.organisations) and everything read at auth-time or by background
        /// services — identity/RBAC, API tokens and the events outbox — which
        /// therefore can't live in a per-tenant database. A named database also
        /// keeps every control-plane call on DocumentForge's scoped
        /// <c>db/{name}</c> surface (namespaced collection names are fully
        /// supported there on all deployed server versions). Injected via
        /// <c>[FromKeyedServices(ControlClientKey)]</c>.
        /// </summary>
        public const string ControlClientKey = "documentforge-control";

        /// <summary>
        /// Registers the DocumentForge clients + typed <see cref="IDocumentStore"/>s:
        /// a per-request <b>tenant-routed</b> main client for business data, a fixed
        /// rules client, and a fixed shared control client/store.
        /// </summary>
        public static IServiceCollection AddDocumentForge(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<DocumentForgeOptions>(configuration.GetSection(DocumentForgeOptions.SectionName));

            // Per-request tenant database routing.
            services.AddScoped<ITenantDatabase, TenantDatabase>();
            services.AddSingleton<IDocumentStoreFactory, DocumentStoreFactory>();

            // Main client + store — TENANT-ROUTED. The database is resolved on every
            // call from ITenantDatabase (the tenant middleware sets it per request;
            // it falls back to the static configured DB). Business repositories inject
            // these unchanged.
            services.AddHttpClient(MainClientName);
            services.AddScoped<IDocumentForgeClient>(sp => new DocumentForgeClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(MainClientName),
                sp.GetRequiredService<IOptions<DocumentForgeOptions>>().Value,
                () => sp.GetRequiredService<ITenantDatabase>().CurrentDatabase));
            services.AddScoped<IDocumentStore, DocumentStore>();

            // Rules client — fixed default database.
            services.AddHttpClient(RulesClientKey);
            services.AddKeyedScoped<IDocumentForgeClient>(RulesClientKey, (sp, _) =>
                new DocumentForgeClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(RulesClientKey),
                    sp.GetRequiredService<IOptions<DocumentForgeOptions>>().Value,
                    database: null));

            // Control client + store — fixed NAMED control database (created at
            // startup by ControlDatabaseInitializer).
            services.AddHttpClient(ControlClientKey);
            services.AddKeyedScoped<IDocumentForgeClient>(ControlClientKey, (sp, _) =>
            {
                var opts = sp.GetRequiredService<IOptions<DocumentForgeOptions>>().Value;
                return new DocumentForgeClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(ControlClientKey),
                    opts,
                    database: opts.ControlDatabase);
            });
            services.AddKeyedScoped<IDocumentStore>(ControlClientKey, (sp, _) =>
                new DocumentStore(sp.GetRequiredKeyedService<IDocumentForgeClient>(ControlClientKey)));

            // Ensure the control database exists before anything reads it (the
            // outbox pump and tenant resolution start immediately).
            services.AddHostedService<ControlDatabaseInitializer>();

            return services;
        }
    }
}
