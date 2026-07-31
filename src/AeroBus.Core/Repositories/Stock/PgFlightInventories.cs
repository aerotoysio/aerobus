using AeroBus.Core.Data.Postgres;
using AeroBus.Core.Model.Stock;
using Npgsql;

namespace AeroBus.Core.Repositories.Stock
{
    /// <summary>
    /// Flight inventory on PostgreSQL — fully relational (no jsonb): these rows
    /// exist to be updated atomically by <see cref="Services.Stock.PgInventoryService"/>.
    /// The flight builder writes them through this same interface.
    /// </summary>
    public sealed class PgFlightInventories(IPgDatabase db) : IFlightInventories
    {
        private const string Cols = "id, company_id, flight_id, bucket, capacity, sold, available, status, created, updated";

        public async Task<FlightInventory?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand($"SELECT {Cols} FROM flight_inventory WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            return (await ReadManyAsync(cmd, ct)).FirstOrDefault();
        }

        public async Task<IReadOnlyList<FlightInventory>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand($"SELECT {Cols} FROM flight_inventory WHERE company_id = @c", conn);
            cmd.Parameters.AddWithValue("c", companyId);
            return await ReadManyAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<FlightInventory>> GetByFlightAsync(Guid flightId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand($"SELECT {Cols} FROM flight_inventory WHERE flight_id = @f", conn);
            cmd.Parameters.AddWithValue("f", flightId);
            return await ReadManyAsync(cmd, ct);
        }

        public async Task<FlightInventory?> SaveAsync(FlightInventory m, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            m = m with { Created = m.Created ?? now, Updated = now };
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO flight_inventory (id, company_id, flight_id, bucket, capacity, sold, available, status, created, updated)
                VALUES (@id, @company, @flight, @bucket, @capacity, @sold, @available, @status, @created, @updated)
                ON CONFLICT (flight_id, bucket) DO UPDATE SET
                    id = EXCLUDED.id,
                    company_id = EXCLUDED.company_id,
                    capacity = EXCLUDED.capacity,
                    sold = EXCLUDED.sold,
                    available = EXCLUDED.available,
                    status = EXCLUDED.status,
                    created = flight_inventory.created,
                    updated = EXCLUDED.updated
                """, conn);
            cmd.Parameters.AddWithValue("id", m.Id);
            cmd.Parameters.AddWithValue("company", (object?)m.CompanyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("flight", m.FlightId);
            cmd.Parameters.AddWithValue("bucket", m.Bucket);
            cmd.Parameters.AddWithValue("capacity", m.Capacity);
            cmd.Parameters.AddWithValue("sold", m.Sold);
            cmd.Parameters.AddWithValue("available", m.Available);
            cmd.Parameters.AddWithValue("status", (object?)m.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("created", (object?)m.Created ?? DBNull.Value);
            cmd.Parameters.AddWithValue("updated", (object?)m.Updated ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            return m;
        }

        public async Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("DELETE FROM flight_inventory WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        private static async Task<IReadOnlyList<FlightInventory>> ReadManyAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            var list = new List<FlightInventory>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new FlightInventory
                {
                    Id = reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0),
                    CompanyId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    FlightId = reader.GetGuid(2),
                    Bucket = reader.GetString(3),
                    Capacity = reader.GetInt32(4),
                    Sold = reader.GetInt32(5),
                    Available = reader.GetInt32(6),
                    Status = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Created = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    Updated = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                });
            }
            return list;
        }
    }
}
