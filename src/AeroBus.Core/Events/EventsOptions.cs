namespace AeroBus.Core.Events
{
    /// <summary>
    /// Dispatcher + retention knobs, bound from the <c>Events</c> config section.
    /// All have sensible defaults so an unconfigured deployment still works.
    /// </summary>
    public sealed class EventsOptions
    {
        /// <summary>How often the dispatcher polls the outbox for dispatchable rows.</summary>
        public int PollSeconds { get; set; } = 2;

        /// <summary>Max delivery attempts before a row is parked as Dead.</summary>
        public int MaxAttempts { get; set; } = 8;

        /// <summary>Exponential backoff base (seconds): delay ≈ Base × 2^(Attempts-1).</summary>
        public int BackoffBaseSeconds { get; set; } = 2;

        /// <summary>Backoff ceiling (seconds) so a stuck row still retries on a bounded cadence.</summary>
        public int BackoffCapSeconds { get; set; } = 300;

        /// <summary>How many dispatchable rows to pull per poll (claimed one at a time).</summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>Per-delivery HTTP timeout for a webhook POST.</summary>
        public int WebhookTimeoutSeconds { get; set; } = 10;

        /// <summary>Retention window (days) for Dispatched/Dead rows — advisory (no reaper wired in Phase 6).</summary>
        public int RetentionDays { get; set; } = 30;
    }
}
