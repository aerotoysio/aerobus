using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AeroBus.Core.Data.Postgres
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the PostgreSQL system-of-record plumbing when
        /// <c>Postgres:ConnectionString</c> is configured: the shared
        /// <see cref="NpgsqlDataSource"/> (singleton pool) and the scoped
        /// tenant-schema-aware <see cref="IPgDatabase"/>. When absent, nothing
        /// registers and the legacy DocumentForge repositories serve the
        /// migrated domains (docs/storage-architecture.md).
        /// </summary>
        public static IServiceCollection AddPostgres(this IServiceCollection services, IConfiguration config)
        {
            var section = config.GetSection(PostgresOptions.SectionName);
            services.Configure<PostgresOptions>(section);
            if (!config.PostgresEnabled()) return services;

            services.AddSingleton(sp =>
                NpgsqlDataSource.Create(sp.GetRequiredService<IOptions<PostgresOptions>>().Value.ConnectionString));
            services.AddScoped<IPgDatabase, PgDatabase>();
            return services;
        }

        /// <summary>Registration-time switch used by migrated modules.</summary>
        public static bool PostgresEnabled(this IConfiguration config) =>
            !string.IsNullOrWhiteSpace(config.GetSection(PostgresOptions.SectionName)["ConnectionString"]);
    }
}
