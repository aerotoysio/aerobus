using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AeroBus.Core.Data.Postgres
{
    public sealed class PostgresOptions
    {
        public const string SectionName = "Postgres";

        /// <summary>
        /// Bootstrap connection string. Empty = Postgres disabled: the legacy
        /// DocumentForge repositories serve the migrated domains instead
        /// (docs/storage-architecture.md).
        /// </summary>
        public string ConnectionString { get; set; } = "";

        public bool Enabled => !string.IsNullOrWhiteSpace(ConnectionString);
    }

    /// <summary>
    /// Tenant-aware access to the ONE aerotoys PostgreSQL database. Isolation is
    /// SCHEMA-PER-ORG (schema = the org slug, mirroring DocumentForge's
    /// database-per-org): every connection opened here gets its
    /// <c>search_path</c> pinned to the current tenant's schema, created and
    /// migrated on first use. Requests with no resolved tenant fail loudly —
    /// never a silent read of someone else's rows.
    /// </summary>
    public interface IPgDatabase
    {
        /// <summary>Open a connection pinned to the current tenant's schema.</summary>
        Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default);

        /// <summary>Open a connection pinned to a SPECIFIC org schema (provisioning, seeds, pumps).</summary>
        Task<NpgsqlConnection> OpenForSchemaAsync(string schema, CancellationToken ct = default);

        /// <summary>Create + migrate an org schema (idempotent). Called at onboarding.</summary>
        Task EnsureSchemaAsync(string schema, CancellationToken ct = default);
    }

    /// <summary>Scoped over the singleton <see cref="NpgsqlDataSource"/> so the
    /// per-request tenant picks the schema; the pool is shared process-wide.</summary>
    public sealed class PgDatabase : IPgDatabase
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ITenantDatabase _tenant;
        private readonly ILogger<PgDatabase> _log;

        // Schemas already ensured this process — EnsureSchema is cheap but not free.
        private static readonly ConcurrentDictionary<string, bool> Ensured = new(StringComparer.Ordinal);

        public PgDatabase(NpgsqlDataSource dataSource, ITenantDatabase tenant, ILogger<PgDatabase> log)
        {
            _dataSource = dataSource;
            _tenant = tenant;
            _log = log;
        }

        private string CurrentSchema =>
            SafeSchema(_tenant.CurrentDatabase
                ?? throw new InvalidOperationException(
                    "No tenant database resolved for this request — refusing to touch Postgres without a schema."));

        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default) =>
            await OpenForSchemaAsync(CurrentSchema, ct);

        public async Task<NpgsqlConnection> OpenForSchemaAsync(string schema, CancellationToken ct = default)
        {
            schema = SafeSchema(schema);
            await EnsureSchemaAsync(schema, ct);
            var conn = await _dataSource.OpenConnectionAsync(ct);
            await using (var cmd = new NpgsqlCommand($"SET search_path TO \"{schema}\"", conn))
                await cmd.ExecuteNonQueryAsync(ct);
            return conn;
        }

        public async Task EnsureSchemaAsync(string schema, CancellationToken ct = default)
        {
            schema = SafeSchema(schema);
            if (Ensured.ContainsKey(schema)) return;

            var conn = await _dataSource.OpenConnectionAsync(ct);
            await using (conn)
            {
                await using (var create = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", conn))
                    await create.ExecuteNonQueryAsync(ct);
                await using (var path = new NpgsqlCommand($"SET search_path TO \"{schema}\"", conn))
                    await path.ExecuteNonQueryAsync(ct);
                await using (var ddl = new NpgsqlCommand(PgDdl.Tables, conn))
                    await ddl.ExecuteNonQueryAsync(ct);
            }
            Ensured[schema] = true;
            _log.LogInformation("Postgres schema '{Schema}' ensured.", schema);
        }

        /// <summary>
        /// Org slugs are lowercase alphanumeric+dash by construction (onboarding
        /// validates); this is the belt-and-braces gate before identifier splicing.
        /// </summary>
        private static string SafeSchema(string schema)
        {
            if (string.IsNullOrWhiteSpace(schema) || schema.Length > 63 ||
                !schema.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_'))
                throw new InvalidOperationException($"'{schema}' is not a valid org schema name.");
            return schema;
        }
    }
}
