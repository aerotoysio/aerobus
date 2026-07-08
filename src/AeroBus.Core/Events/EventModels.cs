using System.Text.Json;
using System.Text.Json.Nodes;
using AeroBus.Core.Model;

namespace AeroBus.Core.Events
{
    /// <summary>
    /// What an event is <em>about</em>: the collection + business id of the
    /// aggregate that changed (e.g. <c>orders</c> / the order's Guid). Kept
    /// deliberately small so it round-trips cleanly through DocumentForge and
    /// out over webhooks/SSE without dragging the whole aggregate along.
    /// </summary>
    public sealed record EventSubject(string Collection, string Id);

    /// <summary>
    /// The public shape a subscriber sees: the stable event contract, minus the
    /// outbox bookkeeping (Status/Attempts/NextAttemptAt/…). This is exactly what
    /// gets HMAC-signed and POSTed to a webhook, and what a
    /// <c>data: &lt;json&gt;</c> SSE frame carries. Deriving it from an
    /// <see cref="OutboxEvent"/> keeps the wire contract independent of the
    /// storage record so the two can evolve separately.
    /// </summary>
    public sealed record EventEnvelope(
        Guid Id,
        Guid? CompanyId,
        long Seq,
        string Type,
        DateTime OccurredAt,
        string? Actor,
        EventSubject Subject,
        JsonNode? Data)
    {
        public static EventEnvelope From(OutboxEvent e) =>
            new(e.Id, e.CompanyId, e.Seq, e.Type, e.OccurredAt, e.Actor,
                new EventSubject(e.Subject?.Collection ?? "", e.Subject?.Id ?? ""),
                e.Data);
    }

    /// <summary>Lifecycle of an outbox row as the dispatcher moves it along.</summary>
    public static class OutboxStatus
    {
        public const string Pending = "Pending";
        public const string Dispatching = "Dispatching";
        public const string Dispatched = "Dispatched";
        public const string Failed = "Failed";
        public const string Dead = "Dead";
    }

    /// <summary>
    /// A single durable event, persisted to the <c>outboxevents</c> collection
    /// the instant the domain writes its change — the transactional outbox that
    /// makes AeroBus's event backbone at-least-once. The dispatcher (a background
    /// service) claims Pending/retryable rows atomically via a DocumentForge
    /// conditional update, delivers them to webhooks + SSE, and advances the
    /// <see cref="Status"/>. The row also doubles as the audit trail
    /// (<c>GET /events</c>).
    ///
    /// <para>
    /// <see cref="Seq"/> is a per-company monotonic counter allocated by
    /// <see cref="IEventPublisher"/> so a subscriber can resume a stream from a
    /// known position (<c>?from={seq}</c>) with no gaps under concurrency.
    /// </para>
    ///
    /// <para>
    /// Publishing is at-least-once with a small crash-loss window: if the process
    /// dies between allocating a Seq and inserting the row, that Seq is skipped
    /// (a gap, never a duplicate). This is accepted per the Phase 6 plan —
    /// correctness of ordering/uniqueness under concurrency is the hard
    /// requirement, not zero-loss durability.
    /// </para>
    /// </summary>
    public sealed record OutboxEvent : IDocument
    {
        public Guid Id { get; init; }

        /// <summary>Owning tenant, or null for cross-tenant/global events (e.g. rule.published).</summary>
        public Guid? CompanyId { get; init; }

        /// <summary>Per-company monotonic sequence (per-"global" for null CompanyId).</summary>
        public long Seq { get; init; }

        public string Type { get; init; } = string.Empty;

        public DateTime OccurredAt { get; init; }

        public string? Actor { get; init; }

        public EventSubject? Subject { get; init; }

        /// <summary>Event payload — the subject + a few key fields, not the whole aggregate.</summary>
        public JsonNode? Data { get; init; }

        // ── dispatcher bookkeeping ────────────────────────────────────────────
        public string Status { get; init; } = OutboxStatus.Pending;
        public int Attempts { get; init; }
        public DateTime? NextAttemptAt { get; init; }
        public DateTime? DispatchedAt { get; init; }

        // ── audit (stamped by DocumentStore) ─────────────────────────────────
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }

    /// <summary>
    /// One row per company (plus one "global" row for null-company events) that
    /// holds the last-allocated <see cref="Seq"/>. The publisher increments it
    /// with an atomic DocumentForge conditional-<c>inc</c>, so two instances
    /// publishing for the same company concurrently always read back distinct,
    /// increasing sequence numbers.
    /// </summary>
    public sealed record EventCursor : IDocument
    {
        public Guid Id { get; init; }

        /// <summary>The tenant this cursor counts for. <see cref="Guid.Empty"/> is the "global" cursor.</summary>
        public Guid CompanyId { get; init; }

        public long Seq { get; init; }

        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }

    /// <summary>
    /// A registered webhook endpoint for a company. On each matching event the
    /// dispatcher POSTs the <see cref="EventEnvelope"/> JSON to <see cref="Url"/>
    /// with an HMAC-SHA256 signature (over the exact body, keyed by
    /// <see cref="Secret"/>) in <c>X-AeroBus-Signature</c>.
    /// </summary>
    public sealed record WebhookSubscription : IDocument
    {
        public Guid Id { get; init; }

        public Guid CompanyId { get; init; }

        public string Url { get; init; } = string.Empty;

        /// <summary>Type filters: exact (<c>order.created</c>) or trailing-glob (<c>order.*</c>).</summary>
        public string[] Types { get; init; } = System.Array.Empty<string>();

        /// <summary>Shared secret for the HMAC signature. Server-generated if the caller omits it.</summary>
        public string Secret { get; init; } = string.Empty;

        public bool Active { get; init; } = true;

        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }

    /// <summary>
    /// Type-pattern matching shared by the dispatcher (webhooks) and the SSE
    /// filter. A pattern is either exact (<c>order.created</c>) or a trailing
    /// glob (<c>order.*</c>, matches <c>order.created</c>, <c>order.changed</c>,
    /// …). <c>*</c> alone matches everything. Case-insensitive.
    /// </summary>
    public static class EventTypeMatch
    {
        public static bool Matches(string pattern, string type)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            if (pattern == "*") return true;
            if (pattern.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = pattern[..^1]; // keep the trailing dot, drop the '*'
                return type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(pattern, type, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesAny(IEnumerable<string> patterns, string type)
        {
            foreach (var p in patterns)
                if (Matches(p, type)) return true;
            return false;
        }
    }
}
