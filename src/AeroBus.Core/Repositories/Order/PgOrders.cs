using AeroBus.Core.Data.Postgres;
using Npgsql;
using OrderModel = AeroBus.Core.Model.Order.Order;

namespace AeroBus.Core.Repositories.Order
{
    /// <summary>
    /// Orders on PostgreSQL (docs/storage-architecture.md): promoted columns
    /// for listing/search/reporting, the full aggregate (items, passengers,
    /// payments, history) in <c>doc jsonb</c>. Same interface, same wire.
    /// </summary>
    public sealed class PgOrders(IPgDatabase db) : IOrders
    {
        public async Task<OrderModel?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT doc FROM orders WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            return await ReadOneAsync(cmd, ct);
        }

        public async Task<OrderModel?> GetByOrderIdAsync(string orderId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT doc FROM orders WHERE order_id = @oid", conn);
            cmd.Parameters.AddWithValue("oid", orderId);
            return await ReadOneAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<OrderModel>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT doc FROM orders WHERE company_id = @c ORDER BY created DESC", conn);
            cmd.Parameters.AddWithValue("c", companyId);
            return await ReadManyAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<OrderModel>> ListByCompanyAsync(
            Guid companyId, string? status, string? search, Guid? profileId,
            int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var sql = "SELECT doc FROM orders WHERE company_id = @c";
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand();
            cmd.Connection = conn;
            cmd.Parameters.AddWithValue("c", companyId);
            if (profileId is { } pid)
            {
                sql += " AND profile_id = @p";
                cmd.Parameters.AddWithValue("p", pid);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                sql += " AND status = @st";
                cmd.Parameters.AddWithValue("st", status);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND order_id ILIKE @q";
                cmd.Parameters.AddWithValue("q", $"%{search.Trim()}%");
            }
            sql += " ORDER BY created DESC LIMIT @lim OFFSET @off";
            cmd.Parameters.AddWithValue("lim", pageSize);
            cmd.Parameters.AddWithValue("off", Math.Max(0, (pageNumber - 1) * pageSize));
            cmd.CommandText = sql;
            return await ReadManyAsync(cmd, ct);
        }

        public async Task<OrderModel?> SaveAsync(OrderModel m, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            return await SaveOnAsync(conn, m, ct);
        }

        /// <summary>Save on an EXISTING connection/transaction — the transactional
        /// order-create pipeline (P9.5) shares one BEGIN…COMMIT across repos.</summary>
        public static async Task<OrderModel> SaveOnAsync(NpgsqlConnection conn, OrderModel m, CancellationToken ct, NpgsqlTransaction? tx = null)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO orders (id, company_id, order_id, status, profile_id, channel, currency, created, updated, doc)
                VALUES (@id, @company, @oid, @status, @profile, @channel, @currency, @created, @updated, @doc::jsonb)
                ON CONFLICT (id) DO UPDATE SET
                    company_id = EXCLUDED.company_id,
                    order_id = EXCLUDED.order_id,
                    status = EXCLUDED.status,
                    profile_id = EXCLUDED.profile_id,
                    channel = EXCLUDED.channel,
                    currency = EXCLUDED.currency,
                    created = orders.created,
                    updated = EXCLUDED.updated,
                    doc = EXCLUDED.doc
                """, conn, tx);
            var currency = m.Payments?.FirstOrDefault()?.Currency;
            cmd.Parameters.AddWithValue("id", m.Id);
            cmd.Parameters.AddWithValue("company", (object?)m.CompanyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("oid", m.OrderId);
            cmd.Parameters.AddWithValue("status", (object?)m.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("profile", (object?)m.ProfileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("channel", (object?)m.Channel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("currency", (object?)currency ?? DBNull.Value);
            cmd.Parameters.AddWithValue("created", (object?)m.Created ?? DBNull.Value);
            cmd.Parameters.AddWithValue("updated", (object?)m.Updated ?? DBNull.Value);
            cmd.Parameters.AddWithValue("doc", PgJson.Serialize(m));
            await cmd.ExecuteNonQueryAsync(ct);
            return m;
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("DELETE FROM orders WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        private static async Task<OrderModel?> ReadOneAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? PgJson.Deserialize<OrderModel>(reader.GetString(0)) : null;
        }

        private static async Task<IReadOnlyList<OrderModel>> ReadManyAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            var list = new List<OrderModel>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (PgJson.Deserialize<OrderModel>(reader.GetString(0)) is { } m)
                    list.Add(m);
            return list;
        }
    }
}
