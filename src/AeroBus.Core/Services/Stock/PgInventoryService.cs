using AeroBus.Core.Data.Postgres;
using Npgsql;

namespace AeroBus.Core.Services.Stock
{
    /// <summary>
    /// Seat-inventory sell/release on PostgreSQL: the DocumentForge
    /// compare-and-set becomes a single guarded atomic
    /// <c>UPDATE … WHERE available &gt;= qty</c> under row-level locking — no
    /// _id resolution, no retry loop, overselling impossible by construction.
    /// </summary>
    public sealed class PgInventoryService(IPgDatabase db) : IInventoryService
    {
        public async Task<InventoryResult> SellAsync(
            Guid companyId, Guid flightId, string bucket, int qty, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            return await SellOnAsync(conn, flightId, bucket, qty, ct);
        }

        public async Task<InventoryResult> ReleaseAsync(
            Guid companyId, Guid flightId, string bucket, int qty, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                UPDATE flight_inventory
                SET available = available + @q, sold = sold - @q, updated = now()
                WHERE flight_id = @f AND bucket = @b AND sold >= @q
                """, conn);
            cmd.Parameters.AddWithValue("q", qty);
            cmd.Parameters.AddWithValue("f", flightId);
            cmd.Parameters.AddWithValue("b", bucket);
            if (await cmd.ExecuteNonQueryAsync(ct) == 1) return InventoryResult.Ok;
            return await ClassifyMissAsync(conn, flightId, bucket, forRelease: true, ct);
        }

        /// <summary>Sell on an EXISTING connection/transaction — the transactional
        /// order-create pipeline (P9.5) shares one BEGIN…COMMIT.</summary>
        public static async Task<InventoryResult> SellOnAsync(
            NpgsqlConnection conn, Guid flightId, string bucket, int qty, CancellationToken ct, NpgsqlTransaction? tx = null)
        {
            await using var cmd = new NpgsqlCommand("""
                UPDATE flight_inventory
                SET available = available - @q, sold = sold + @q, updated = now()
                WHERE flight_id = @f AND bucket = @b AND available >= @q
                """, conn, tx);
            cmd.Parameters.AddWithValue("q", qty);
            cmd.Parameters.AddWithValue("f", flightId);
            cmd.Parameters.AddWithValue("b", bucket);
            if (await cmd.ExecuteNonQueryAsync(ct) == 1) return InventoryResult.Ok;
            return await ClassifyMissAsync(conn, flightId, bucket, forRelease: false, ct, tx);
        }

        private static async Task<InventoryResult> ClassifyMissAsync(
            NpgsqlConnection conn, Guid flightId, string bucket, bool forRelease, CancellationToken ct, NpgsqlTransaction? tx = null)
        {
            await using var probe = new NpgsqlCommand(
                "SELECT available, sold FROM flight_inventory WHERE flight_id = @f AND bucket = @b", conn, tx);
            probe.Parameters.AddWithValue("f", flightId);
            probe.Parameters.AddWithValue("b", bucket);
            await using var reader = await probe.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return InventoryResult.NoInventory;
            var available = reader.GetInt32(0);
            if (forRelease) return InventoryResult.Insufficient; // row exists, sold < qty
            return available <= 0 ? InventoryResult.SoldOut : InventoryResult.Insufficient;
        }
    }
}
