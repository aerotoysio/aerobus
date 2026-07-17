using System.Globalization;
using System.Text.Json;
using AeroBus.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Events
{
    /// <summary>
    /// Reads + atomic claims over the <c>outboxevents</c> collection. The plain
    /// CRUD comes from <see cref="DocumentRepository{T}"/>; this adds the three
    /// bespoke shapes the backbone needs — the dispatcher's dispatchable poll, the
    /// per-company audit/list query, and the SSE tail — plus the CAS-based
    /// <see cref="TryClaimAsync"/> that makes dispatch multi-instance safe.
    /// </summary>
    public interface IOutbox
    {
        Task<OutboxEvent?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<OutboxEvent?> SaveAsync(OutboxEvent model, CancellationToken ct = default);

        /// <summary>
        /// Rows the dispatcher may work now: Status=Pending, or Status=Failed with
        /// NextAttemptAt due. Ordered by Seq, capped at <paramref name="limit"/>.
        /// </summary>
        Task<IReadOnlyList<OutboxEvent>> GetDispatchableAsync(DateTime nowUtc, int limit, CancellationToken ct = default);

        /// <summary>Per-company audit query (the outbox doubles as the audit trail).</summary>
        Task<IReadOnlyList<OutboxEvent>> ListForCompanyAsync(
            Guid companyId, string? type, string? status, long fromSeq, int limit, CancellationToken ct = default);

        /// <summary>SSE tail: rows for a company with Seq &gt; <paramref name="fromSeq"/>, ordered, capped.</summary>
        Task<IReadOnlyList<OutboxEvent>> TailForCompanyAsync(
            Guid companyId, long fromSeq, int limit, CancellationToken ct = default);

        /// <summary>
        /// Atomically move a row from an expected status into Dispatching (bumping
        /// Attempts), guarding on the current Status via a DocumentForge conditional
        /// update. Returns true iff THIS caller won the claim; false (409) means
        /// another instance already claimed it — skip. This is the multi-instance
        /// safety primitive for dispatch.
        /// </summary>
        Task<bool> TryClaimAsync(OutboxEvent row, CancellationToken ct = default);
    }

    public sealed class Outbox(
        [FromKeyedServices(AeroBus.Core.Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentStore store,
        [FromKeyedServices(AeroBus.Core.Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentForgeClient df)
        : DocumentRepository<OutboxEvent>(store), IOutbox
    {
        private readonly IDocumentForgeClient _df = df;

        protected override string Collection => DfCollections.Events.Outbox;

        public Task<IReadOnlyList<OutboxEvent>> GetDispatchableAsync(DateTime nowUtc, int limit, CancellationToken ct = default)
        {
            var nowLit = Iso(nowUtc);
            // Pending (always due), OR Failed whose backoff has elapsed.
            var where =
                $"(Status = '{OutboxStatus.Pending}') OR " +
                $"(Status = '{OutboxStatus.Failed}' AND NextAttemptAt <= '{nowLit}') " +
                "ORDER BY Seq ASC";
            return QueryWhereAsync(where, size: limit, ct: ct);
        }

        public Task<IReadOnlyList<OutboxEvent>> ListForCompanyAsync(
            Guid companyId, string? type, string? status, long fromSeq, int limit, CancellationToken ct = default)
        {
            var where = $"CompanyId = '{companyId}' AND Seq > {fromSeq}";
            if (!string.IsNullOrWhiteSpace(type))
                where += $" AND Type = '{Escape(type)}'";
            if (!string.IsNullOrWhiteSpace(status))
                where += $" AND Status = '{Escape(status)}'";
            where += " ORDER BY Seq ASC";
            return QueryWhereAsync(where, size: limit, ct: ct);
        }

        public Task<IReadOnlyList<OutboxEvent>> TailForCompanyAsync(
            Guid companyId, long fromSeq, int limit, CancellationToken ct = default) =>
            QueryWhereAsync($"CompanyId = '{companyId}' AND Seq > {fromSeq} ORDER BY Seq ASC", size: limit, ct: ct);

        public async Task<bool> TryClaimAsync(OutboxEvent row, CancellationToken ct = default)
        {
            var docId = await ResolveIdAsync(row.Id, ct);
            if (docId is null) return false; // row vanished (retention?) — nothing to claim

            var result = await _df.ConditionalUpdateAsync(
                Collection,
                docId,
                conditions: new[] { new DocumentForgeCondition("Status", "==", row.Status) },
                operations: new[]
                {
                    new DocumentForgeMutation("Status", "set", OutboxStatus.Dispatching),
                    new DocumentForgeMutation("Attempts", "inc", 1),
                },
                ct);

            // 409 => Status changed under us (another instance claimed it): not ours.
            // 404 => row gone: not ours.
            return result.Success;
        }

        /// <summary>Resolve a business Id → DocumentForge internal _id (needed by the CAS claim).</summary>
        private async Task<string?> ResolveIdAsync(Guid id, CancellationToken ct)
        {
            var rows = await _df.QueryAsync($"SELECT * FROM {Collection} WHERE Id = '{id}'", ct);
            if (rows.Count == 0) return null;
            return rows[0].TryGetProperty("_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
        }

        private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        private static string Escape(string s) => s.Replace("'", "''");
    }
}
