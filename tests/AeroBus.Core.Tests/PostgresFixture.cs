using AeroBus.Core.Data;
using AeroBus.Core.Data.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Live PostgreSQL fixture (docs/storage-architecture.md). Like the
/// DocumentForge fixture it FAILS RED when the server is missing rather than
/// silently passing: set <c>AEROTOYS_PG</c> to a connection string
/// (Host=localhost;Database=aerotoys;Username=postgres;Password=…). Each run
/// works in a throwaway schema that is dropped on dispose.
/// </summary>
public sealed class PostgresFixture : IDisposable
{
    private sealed class StaticTenant(string db) : ITenantDatabase
    {
        public string? CurrentDatabase { get; set; } = db;
        public bool IsTenantResolved => true;
    }

    public NpgsqlDataSource DataSource { get; }
    public IPgDatabase Db { get; }
    public string Schema { get; }

    public PostgresFixture()
    {
        var conn = Environment.GetEnvironmentVariable("AEROTOYS_PG");
        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException(
                "AEROTOYS_PG is not set. These are live round-trip tests — point it at a local " +
                "PostgreSQL (Host=localhost;Database=aerotoys;Username=postgres;Password=…).");

        Schema = $"testrun_{Guid.NewGuid():N}"[..20];
        DataSource = NpgsqlDataSource.Create(conn);
        Db = new PgDatabase(DataSource, new StaticTenant(Schema), NullLogger<PgDatabase>.Instance);
        Db.EnsureSchemaAsync(Schema).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        using var conn = DataSource.OpenConnection();
        using (var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE", conn))
            cmd.ExecuteNonQuery();
        DataSource.Dispose();
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
