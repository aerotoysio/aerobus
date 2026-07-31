using AeroBus.Core.Data.Postgres;
using Npgsql;
using CustomerModel = AeroBus.Core.Model.Customer.Customer;

namespace AeroBus.Core.Repositories.Customer
{
    /// <summary>
    /// Customers on PostgreSQL (docs/storage-architecture.md): promoted columns
    /// for identity lookups and search, the full aggregate (passports, stored
    /// cards) in <c>doc jsonb</c>. Same interface, same wire shapes — the swap
    /// is invisible above the repository.
    /// </summary>
    public sealed class PgCustomers(IPgDatabase db) : ICustomers
    {
        private const string Cols = "id, doc";

        public async Task<CustomerModel?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand($"SELECT {Cols} FROM customers WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            return await ReadOneAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<CustomerModel>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                $"SELECT {Cols} FROM customers WHERE company_id = @c ORDER BY created DESC", conn);
            cmd.Parameters.AddWithValue("c", companyId);
            return await ReadManyAsync(cmd, ct);
        }

        public async Task<CustomerModel?> GetByNumberAsync(string customerNumber, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                $"SELECT {Cols} FROM customers WHERE customer_number = @n", conn);
            cmd.Parameters.AddWithValue("n", customerNumber);
            return await ReadOneAsync(cmd, ct);
        }

        public async Task<CustomerModel?> FindByEmailAsync(Guid companyId, string email, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                $"SELECT {Cols} FROM customers WHERE company_id = @c AND lower(email) = lower(@e) LIMIT 1", conn);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("e", email.Trim());
            return await ReadOneAsync(cmd, ct);
        }

        public async Task<CustomerModel?> FindByPhoneAndLastNameAsync(
            Guid companyId, string phone, string lastName, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                $"SELECT {Cols} FROM customers WHERE company_id = @c AND lower(phone) = lower(@p) AND lower(last_name) = lower(@l) LIMIT 1", conn);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("p", phone.Trim());
            cmd.Parameters.AddWithValue("l", lastName.Trim());
            return await ReadOneAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<CustomerModel>> ListByCompanyAsync(
            Guid companyId, string? loyaltyProgram, string? status, string? search,
            int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var sql = $"SELECT {Cols} FROM customers WHERE company_id = @c";
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand();
            cmd.Connection = conn;
            cmd.Parameters.AddWithValue("c", companyId);
            if (!string.IsNullOrWhiteSpace(loyaltyProgram))
            {
                sql += " AND loyalty_program = @lp";
                cmd.Parameters.AddWithValue("lp", loyaltyProgram);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                sql += " AND status = @st";
                cmd.Parameters.AddWithValue("st", status);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (first_name ILIKE @q OR last_name ILIKE @q OR email ILIKE @q OR customer_number ILIKE @q)";
                cmd.Parameters.AddWithValue("q", $"%{search.Trim()}%");
            }
            sql += " ORDER BY created DESC LIMIT @lim OFFSET @off";
            cmd.Parameters.AddWithValue("lim", pageSize);
            cmd.Parameters.AddWithValue("off", Math.Max(0, (pageNumber - 1) * pageSize));
            cmd.CommandText = sql;
            return await ReadManyAsync(cmd, ct);
        }

        public async Task<CustomerModel?> SaveAsync(CustomerModel m, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            m = m with { Created = m.Created ?? now, Updated = now };

            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO customers (id, company_id, customer_number, first_name, last_name,
                                       email, phone, loyalty_program, status, created, updated, doc)
                VALUES (@id, @company, @number, @first, @last, @email, @phone, @loyalty, @status, @created, @updated, @doc::jsonb)
                ON CONFLICT (id) DO UPDATE SET
                    company_id = EXCLUDED.company_id,
                    customer_number = EXCLUDED.customer_number,
                    first_name = EXCLUDED.first_name,
                    last_name = EXCLUDED.last_name,
                    email = EXCLUDED.email,
                    phone = EXCLUDED.phone,
                    loyalty_program = EXCLUDED.loyalty_program,
                    status = EXCLUDED.status,
                    created = customers.created,
                    updated = EXCLUDED.updated,
                    doc = EXCLUDED.doc
                """, conn);
            cmd.Parameters.AddWithValue("id", m.Id);
            cmd.Parameters.AddWithValue("company", (object?)m.CompanyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("number", (object?)m.CustomerNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("first", m.FirstName);
            cmd.Parameters.AddWithValue("last", m.LastName);
            cmd.Parameters.AddWithValue("email", (object?)m.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("phone", (object?)m.Phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("loyalty", (object?)m.LoyaltyProgram ?? DBNull.Value);
            cmd.Parameters.AddWithValue("status", m.Status);
            cmd.Parameters.AddWithValue("created", (object?)m.Created ?? DBNull.Value);
            cmd.Parameters.AddWithValue("updated", (object?)m.Updated ?? DBNull.Value);
            cmd.Parameters.AddWithValue("doc", PgJson.Serialize(m));
            await cmd.ExecuteNonQueryAsync(ct);
            return m;
        }

        public async Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("DELETE FROM customers WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        private static async Task<CustomerModel?> ReadOneAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? PgJson.Deserialize<CustomerModel>(reader.GetString(1)) : null;
        }

        private static async Task<IReadOnlyList<CustomerModel>> ReadManyAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            var list = new List<CustomerModel>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (PgJson.Deserialize<CustomerModel>(reader.GetString(1)) is { } m)
                    list.Add(m);
            return list;
        }
    }
}
