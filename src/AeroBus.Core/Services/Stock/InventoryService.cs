using System.Collections.Concurrent;
using System.Text.Json;
using AeroBus.Core.Data;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Stock
{
    /// <summary>
    /// Outcome of a sell/release against the seat inventory. <see cref="Success"/>
    /// is the only thing callers must branch on; <see cref="Reason"/> is a stable
    /// machine-readable code for logging / surfacing a 409:
    /// <list type="bullet">
    ///   <item><c>ok</c> — the counters moved.</item>
    ///   <item><c>soldOut</c> — Available reached 0 (guard failed on a full flight).</item>
    ///   <item><c>insufficient</c> — Available &gt; 0 but &lt; requested qty.</item>
    ///   <item><c>noInventory</c> — no flightinventory document for (FlightId, Bucket).</item>
    /// </list>
    /// </summary>
    public sealed record InventoryResult(bool Success, string Reason)
    {
        public static readonly InventoryResult Ok = new(true, "ok");
        public static InventoryResult SoldOut => new(false, "soldOut");
        public static InventoryResult Insufficient => new(false, "insufficient");
        public static InventoryResult NoInventory => new(false, "noInventory");
    }

    public interface IInventoryService
    {
        /// <summary>
        /// Atomically decrement seat availability for (flightId, bucket) by
        /// <paramref name="qty"/>, guarding on <c>Available &gt;= qty</c> so the
        /// flight can never oversell. Never throws for the contended outcomes.
        /// </summary>
        Task<InventoryResult> SellAsync(Guid companyId, Guid flightId, string bucket, int qty, CancellationToken ct = default);

        /// <summary>
        /// Atomically return seats to the pool (inverse of <see cref="SellAsync"/>),
        /// guarding on <c>Sold &gt;= qty</c> so a double-release can't drive Sold
        /// negative. Used on cancel/refund.
        /// </summary>
        Task<InventoryResult> ReleaseAsync(Guid companyId, Guid flightId, string bucket, int qty, CancellationToken ct = default);
    }

    /// <summary>
    /// Seat-inventory sell/release over the DocumentForge conditional-update
    /// (compare-and-set) primitive. New in Phase 5 — the counters
    /// (<c>Available</c>/<c>Sold</c>) are top-level scalars on the flightinventory
    /// document precisely so this atomic guard-and-mutate is possible under the
    /// engine write lock, which is what makes overselling impossible under
    /// concurrency.
    ///
    /// The conditional update needs DocumentForge's internal <c>_id</c> (a Guid,
    /// distinct from the business "Id"), so this service resolves (FlightId,Bucket)
    /// → _id via a raw <see cref="IDocumentForgeClient.QueryAsync(string, CancellationToken)"/>
    /// (SELECT * surfaces _id on every row) and caches it with a short TTL. On a
    /// 404 (stale/rotated id) it re-resolves once and retries.
    /// </summary>
    public sealed class InventoryService : IInventoryService
    {
        private const string Collection = "flightinventory";
        private static readonly TimeSpan IdCacheTtl = TimeSpan.FromSeconds(30);

        // (FlightId,Bucket) → DocumentForge _id, shared process-wide (singleton) so
        // the cache is useful across scoped requests. IHotCache can't express a
        // per-entry keyed TTL (it is a typed whole-list snapshot cache), so a small
        // dedicated map is used here.
        private static readonly ConcurrentDictionary<string, (string Id, DateTime ExpiresUtc)> IdCache = new();

        private readonly IDocumentForgeClient _df;
        private readonly ILogger<InventoryService> _log;

        public InventoryService(IDocumentForgeClient df, ILogger<InventoryService> log)
        {
            _df = df;
            _log = log;
        }

        public Task<InventoryResult> SellAsync(Guid companyId, Guid flightId, string bucket, int qty, CancellationToken ct = default) =>
            MutateAsync(companyId, flightId, bucket, qty, sell: true, ct);

        public Task<InventoryResult> ReleaseAsync(Guid companyId, Guid flightId, string bucket, int qty, CancellationToken ct = default) =>
            MutateAsync(companyId, flightId, bucket, qty, sell: false, ct);

        private async Task<InventoryResult> MutateAsync(
            Guid companyId, Guid flightId, string bucket, int qty, bool sell, CancellationToken ct)
        {
            if (qty <= 0) return InventoryResult.Ok; // nothing to move
            bucket = string.IsNullOrWhiteSpace(bucket) ? "ALL" : bucket;

            // sell:    guard Available >= qty,  ops: dec Available, inc Sold
            // release: guard Sold      >= qty,  ops: inc Available, dec Sold
            var guardField = sell ? "Available" : "Sold";
            var conditions = new[] { new DocumentForgeCondition(guardField, ">=", qty) };
            var operations = sell
                ? new[]
                {
                    new DocumentForgeMutation("Available", "dec", qty),
                    new DocumentForgeMutation("Sold", "inc", qty),
                }
                : new[]
                {
                    new DocumentForgeMutation("Available", "inc", qty),
                    new DocumentForgeMutation("Sold", "dec", qty),
                };

            // Resolve the internal _id (cached), then conditional-update. On a 404
            // the cached id is stale/rotated — re-resolve once (bypassing cache) and
            // retry a single time.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var forceRefresh = attempt == 1;
                var docId = await ResolveIdAsync(flightId, bucket, forceRefresh, ct);
                if (docId is null)
                    return InventoryResult.NoInventory;

                var result = await _df.ConditionalUpdateAsync(Collection, docId, conditions, operations, ct);

                if (result.Success)
                {
                    // events: inventory.adjusted via outbox in Phase 6
                    return InventoryResult.Ok;
                }

                if (result.StatusCode == 404)
                {
                    Invalidate(flightId, bucket);
                    continue; // re-resolve + retry once
                }

                // 409 guard failed: distinguish sold-out (0 left) from a partial
                // shortfall (some left, but < qty). Read Available FRESH here (not a
                // snapshot taken before the CAS, which can be stale under contention)
                // so the label is deterministic — this extra read is only paid on the
                // rare contended-failure path, never on the success path.
                if (result.StatusCode == 409 && sell)
                {
                    var available = await ReadAvailableAsync(flightId, bucket, ct);
                    return available <= 0 ? InventoryResult.SoldOut : InventoryResult.Insufficient;
                }

                // 409 on release means Sold < qty (already released) — treat as a
                // no-op success so a double-release is idempotent rather than an error.
                if (result.StatusCode == 409 && !sell)
                {
                    _log.LogInformation(
                        "Inventory release for flight {FlightId}/{Bucket} guard Sold>={Qty} failed (already released); treating as no-op.",
                        flightId, bucket, qty);
                    return InventoryResult.Ok;
                }

                return new InventoryResult(false, $"conditionFailed:{result.FailedCondition}");
            }

            // Both attempts 404'd — the document genuinely doesn't exist.
            return InventoryResult.NoInventory;
        }

        /// <summary>
        /// Resolve (flightId, bucket) → DocumentForge internal <c>_id</c>, using the
        /// short-TTL id cache. A cache miss (or forced refresh after a stale-id 404)
        /// queries the flightinventory doc; SELECT * surfaces <c>_id</c> on the row.
        /// The <c>_id</c> is stable across conditional updates, so it caches safely.
        /// </summary>
        private async Task<string?> ResolveIdAsync(Guid flightId, string bucket, bool forceRefresh, CancellationToken ct)
        {
            var key = CacheKey(flightId, bucket);

            if (!forceRefresh &&
                IdCache.TryGetValue(key, out var cached) &&
                cached.ExpiresUtc > DateTime.UtcNow)
                return cached.Id;

            var flightLit = flightId.ToString().Replace("'", "''");
            var bucketLit = bucket.Replace("'", "''");
            var rows = await _df.QueryAsync(
                $"SELECT * FROM {Collection} WHERE FlightId = '{flightLit}' AND Bucket = '{bucketLit}'", ct);

            if (rows.Count == 0)
            {
                Invalidate(flightId, bucket);
                return null;
            }

            var docId = rows[0].TryGetProperty("_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;

            if (docId is not null)
                IdCache[key] = (docId, DateTime.UtcNow.Add(IdCacheTtl));

            return docId;
        }

        /// <summary>Reads the current Available for (flightId, bucket); 0 if the
        /// document is gone. Advisory only — used to label a contended 409.</summary>
        private async Task<int> ReadAvailableAsync(Guid flightId, string bucket, CancellationToken ct)
        {
            var flightLit = flightId.ToString().Replace("'", "''");
            var bucketLit = bucket.Replace("'", "''");
            var rows = await _df.QueryAsync(
                $"SELECT * FROM {Collection} WHERE FlightId = '{flightLit}' AND Bucket = '{bucketLit}'", ct);
            if (rows.Count == 0) return 0;
            return rows[0].TryGetProperty("Available", out var availEl) && availEl.ValueKind == JsonValueKind.Number
                ? availEl.GetInt32()
                : 0;
        }

        private static void Invalidate(Guid flightId, string bucket) =>
            IdCache.TryRemove(CacheKey(flightId, bucket), out _);

        private static string CacheKey(Guid flightId, string bucket) => $"{flightId:N}|{bucket}";
    }
}
