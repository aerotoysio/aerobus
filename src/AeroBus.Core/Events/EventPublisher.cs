using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using AeroBus.Core.Data;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Events
{
    /// <summary>
    /// Publishes domain events into the transactional outbox. The one seam the
    /// domain services depend on to emit — everything else (dispatch, delivery,
    /// retry) happens behind the outbox, asynchronously.
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>
        /// Allocate the next per-company <see cref="OutboxEvent.Seq"/> and insert a
        /// Pending outbox row. Best-effort-but-loud: a failure here is logged and
        /// swallowed (the domain write that triggered it has already happened), so
        /// this never throws out to a caller. Returns the persisted event, or null
        /// if publishing failed.
        /// </summary>
        Task<OutboxEvent?> PublishAsync(
            string type,
            EventSubject subject,
            object data,
            Guid? companyId,
            string? actor = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Outbox publisher over DocumentForge.
    ///
    /// <para><b>Sequence allocation (the race-safe bit).</b> Each company (plus a
    /// single "global" bucket for null-company events) has one <c>eventcursors</c>
    /// document holding the last-issued <c>Seq</c>. To allocate, the publisher runs
    /// an atomic conditional-<c>inc</c> on that doc — DocumentForge applies the
    /// increment under its engine write lock and returns the <em>post-increment</em>
    /// document, so the new Seq is read straight off the CAS result. Two instances
    /// incrementing the same cursor concurrently therefore always read back
    /// distinct, strictly-increasing values (no gaps, no dupes). The cursor's
    /// internal <c>_id</c> is resolved via a raw query and cached with a short TTL,
    /// re-resolved on a stale 404 — mirroring InventoryService.</para>
    ///
    /// <para><b>Create/inc race on first use.</b> If the cursor doc doesn't exist
    /// yet the first inc 404s; the publisher then seeds it (Seq=1) and uses 1. Two
    /// instances can both try to seed — the loser's insert is tolerated and we fall
    /// back to inc, so both still get distinct values.</para>
    /// </summary>
    public sealed class EventPublisher : IEventPublisher
    {
        private const string CursorCollection = "eventcursors";
        private const string OutboxCollection = "outboxevents";
        private static readonly TimeSpan IdCacheTtl = TimeSpan.FromSeconds(30);

        // CompanyId (Guid.Empty == global) → cursor doc's DocumentForge _id. Static
        // so the cache is useful across scoped requests, like InventoryService.
        private static readonly ConcurrentDictionary<Guid, (string Id, DateTime ExpiresUtc)> CursorIdCache = new();

        // Per-company seeding gate: collapses the N concurrent first-publishers within
        // THIS process to a single seed insert, so a fresh company yields exactly one
        // cursor row (DocumentForge does not enforce Id uniqueness on insert, so
        // unguarded concurrent seeds would each create a duplicate row). Across
        // processes, DedupeCursorsAsync converges any residual duplicates.
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SeedGates = new();

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private readonly IDocumentForgeClient _df;
        private readonly IDocumentStore _store;
        private readonly ILogger<EventPublisher> _log;

        public EventPublisher(IDocumentForgeClient df, IDocumentStore store, ILogger<EventPublisher> log)
        {
            _df = df;
            _store = store;
            _log = log;
        }

        public async Task<OutboxEvent?> PublishAsync(
            string type,
            EventSubject subject,
            object data,
            Guid? companyId,
            string? actor = null,
            CancellationToken ct = default)
        {
            try
            {
                var seq = await AllocateSeqAsync(companyId ?? Guid.Empty, ct);

                var evt = new OutboxEvent
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Seq = seq,
                    Type = type,
                    OccurredAt = DateTime.UtcNow,
                    Actor = actor,
                    Subject = subject,
                    Data = ToNode(data),
                    Status = OutboxStatus.Pending,
                    Attempts = 0,
                    NextAttemptAt = DateTime.UtcNow,
                };

                // UpsertAsync stamps Created/Updated and inserts the row.
                var saved = await _store.UpsertAsync(OutboxCollection, evt, evt.Id, ct);
                _log.LogInformation(
                    "Published event {Type} seq {Seq} for company {CompanyId} (subject {Collection}/{SubjectId}).",
                    type, seq, companyId, subject.Collection, subject.Id);
                return saved;
            }
            catch (Exception ex)
            {
                // Publishing must NEVER fail the domain operation that triggered it —
                // the order/flight/etc. is already persisted. Log loudly and swallow.
                // (At-least-once with possible loss on a crash-between-writes is
                // accepted per the Phase 6 plan.)
                _log.LogError(ex,
                    "Failed to publish event {Type} for company {CompanyId} (subject {Collection}/{SubjectId}); the domain change stands, the event is lost.",
                    type, companyId, subject.Collection, subject.Id);
                return null;
            }
        }

        /// <summary>
        /// Allocate the next Seq for <paramref name="companyId"/> (Guid.Empty ==
        /// global). Correctness rests on one invariant: <em>every</em> Seq is issued
        /// by an atomic conditional-<c>inc</c> against a single cursor document. The
        /// cursor is seeded once at <c>Seq=0</c>, so the first inc returns 1, the next
        /// 2, and so on — distinct and contiguous under any concurrency, because the
        /// increment executes server-side under DocumentForge's write lock and returns
        /// the post-increment value.
        /// </summary>
        private async Task<long> AllocateSeqAsync(Guid companyId, CancellationToken ct)
        {
            // Up to 3 passes: cached id → stale-404 re-resolve → post-seed. Each pass
            // ensures a cursor exists, then does one atomic inc.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var forceRefresh = attempt > 0;
                var cursorId = await ResolveCursorIdAsync(companyId, forceRefresh, ct)
                               ?? await EnsureCursorAsync(companyId, ct);

                var result = await _df.ConditionalUpdateAsync(
                    CursorCollection,
                    cursorId,
                    conditions: new[] { new DocumentForgeCondition("Seq", "exists") },
                    operations: new[] { new DocumentForgeMutation("Seq", "inc", 1) },
                    ct);

                if (result.Success)
                    return ReadSeq(result.Document);

                if (result.StatusCode == 404)
                {
                    // Resolved id was stale/rotated — drop it and re-resolve/seed.
                    Invalidate(companyId);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Event cursor inc for company {companyId} failed unexpectedly (status {result.StatusCode}, failedCondition {result.FailedCondition}).");
            }

            throw new InvalidOperationException(
                $"Could not allocate a Seq for company {companyId} after re-resolving the cursor.");
        }

        /// <summary>
        /// Ensure the cursor document exists (seeded at Seq=0) and return its
        /// DocumentForge <c>_id</c>. Seeding is serialized per company by a process
        /// semaphore so N concurrent first-publishers produce exactly one row; if a
        /// residual cross-process duplicate slips in, it is collapsed deterministically
        /// (keep the smallest <c>_id</c>) so all instances converge on the same cursor.
        /// </summary>
        private async Task<string> EnsureCursorAsync(Guid companyId, CancellationToken ct)
        {
            var gate = SeedGates.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                // Someone may have seeded while we waited on the gate.
                var existing = await DedupeCursorsAsync(companyId, ct);
                if (existing is not null)
                {
                    CacheId(companyId, existing);
                    return existing;
                }

                var cursor = new EventCursor
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Seq = 0, // first inc yields 1
                };
                await _store.UpsertAsync(CursorCollection, cursor, cursor.Id, ct);

                // Re-read (and dedupe any cross-process duplicate) to get the canonical _id.
                var id = await DedupeCursorsAsync(companyId, ct)
                         ?? throw new InvalidOperationException($"Cursor seed for company {companyId} did not materialise.");
                CacheId(companyId, id);
                return id;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Return the canonical cursor <c>_id</c> for a company, collapsing duplicates
        /// deterministically: if more than one cursor row exists (a cross-process seed
        /// race — DocumentForge does not enforce Id uniqueness), keep the row with the
        /// lexicographically-smallest <c>_id</c> and delete the rest. Every instance
        /// picks the same survivor, so they all increment the same counter. Returns
        /// null if no cursor exists yet.
        /// </summary>
        private async Task<string?> DedupeCursorsAsync(Guid companyId, CancellationToken ct)
        {
            var rows = await _df.QueryAsync(
                $"SELECT * FROM {CursorCollection} WHERE CompanyId = '{companyId}'", ct);
            if (rows.Count == 0) return null;

            var ids = rows
                .Select(r => ExtractId(r))
                .Where(id => id is not null)
                .Select(id => id!)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0) return null;

            var survivor = ids[0];
            for (var i = 1; i < ids.Count; i++)
            {
                // Best-effort delete of the losing duplicate by its business Id. This
                // only runs on the rare cross-process seed race.
                var loserBusinessId = BusinessId(rows, ids[i]);
                if (loserBusinessId is not null)
                    try { await _df.DeleteByFieldAsync(CursorCollection, "Id", loserBusinessId, ct); }
                    catch { /* inert duplicate; ignore */ }
            }
            return survivor;
        }

        private static string? BusinessId(IReadOnlyList<JsonElement> rows, string internalId)
        {
            foreach (var r in rows)
                if (ExtractId(r) == internalId &&
                    r.TryGetProperty("Id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    return idEl.GetString();
            return null;
        }

        // ── cursor _id resolution (mirrors InventoryService) ──────────────────

        private async Task<string?> ResolveCursorIdAsync(Guid companyId, bool forceRefresh, CancellationToken ct)
        {
            if (!forceRefresh &&
                CursorIdCache.TryGetValue(companyId, out var cached) &&
                cached.ExpiresUtc > DateTime.UtcNow)
                return cached.Id;

            var id = await QueryCursorIdAsync(companyId, ct);
            if (id is not null) CacheId(companyId, id);
            else Invalidate(companyId);
            return id;
        }

        private async Task<string?> QueryCursorIdAsync(Guid companyId, CancellationToken ct)
        {
            var rows = await _df.QueryAsync(
                $"SELECT * FROM {CursorCollection} WHERE CompanyId = '{companyId}'", ct);
            return rows.Count == 0 ? null : ExtractId(rows[0]);
        }

        private static string? ExtractId(JsonElement? row)
        {
            if (row is not { } r) return null;
            return r.TryGetProperty("_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
        }

        private static long ReadSeq(JsonElement? doc)
        {
            if (doc is { } d && d.TryGetProperty("Seq", out var seqEl) && seqEl.ValueKind == JsonValueKind.Number)
                return seqEl.GetInt64();
            throw new InvalidOperationException("Event cursor inc succeeded but the returned document had no numeric Seq.");
        }

        private static void CacheId(Guid companyId, string id) =>
            CursorIdCache[companyId] = (id, DateTime.UtcNow.Add(IdCacheTtl));

        private static void Invalidate(Guid companyId) =>
            CursorIdCache.TryRemove(companyId, out _);

        private static JsonNode? ToNode(object data)
        {
            if (data is JsonNode node) return node;
            return JsonSerializer.SerializeToNode(data, data.GetType(), Json);
        }
    }
}
