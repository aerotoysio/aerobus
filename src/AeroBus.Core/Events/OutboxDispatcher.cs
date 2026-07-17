using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Events
{
    /// <summary>
    /// The outbox pump. Polls <c>outboxevents</c> for dispatchable rows every
    /// <see cref="EventsOptions.PollSeconds"/>, claims each one atomically (so two
    /// instances never deliver the same row), fans it out to every matching webhook
    /// subscription, and advances its status — Dispatched on success, or Failed with
    /// an exponential backoff, escalating to Dead after
    /// <see cref="EventsOptions.MaxAttempts"/> attempts.
    ///
    /// <para>Correctness over throughput: rows are claimed one at a time via a
    /// compare-and-set on Status, which is what makes the dispatcher multi-instance
    /// safe. SSE subscribers read the outbox directly (by Seq) and so are decoupled
    /// from this loop entirely.</para>
    /// </summary>
    public sealed class OutboxDispatcher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly EventsOptions _opts;
        private readonly ILogger<OutboxDispatcher> _log;

        public OutboxDispatcher(
            IServiceScopeFactory scopes,
            IOptions<EventsOptions> opts,
            ILogger<OutboxDispatcher> log)
        {
            _scopes = scopes;
            _opts = opts.Value;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation(
                "Outbox dispatcher started (poll {Poll}s, maxAttempts {Max}, backoff {Base}s..{Cap}s).",
                _opts.PollSeconds, _opts.MaxAttempts, _opts.BackoffBaseSeconds, _opts.BackoffCapSeconds);

            var delay = TimeSpan.FromSeconds(Math.Max(1, _opts.PollSeconds));
            var consecutiveFailures = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = delay;
                try
                {
                    await PumpOnceAsync(stoppingToken);
                    if (consecutiveFailures > 0)
                        _log.LogInformation("Outbox dispatch recovered after {Failures} failed cycle(s).", consecutiveFailures);
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A poll-loop error must not kill the pump; log and keep going —
                    // but don't stack-trace every poll while the store is down
                    // (e.g. DocumentForge not running locally): full detail on the
                    // first failure, then compact one-liners with an exponentially
                    // backed-off poll (capped) until it recovers.
                    consecutiveFailures++;
                    var backoffSeconds = Math.Min(
                        _opts.BackoffCapSeconds,
                        Math.Max(1, _opts.PollSeconds) * Math.Pow(2, Math.Min(consecutiveFailures - 1, 6)));
                    wait = TimeSpan.FromSeconds(backoffSeconds);

                    if (consecutiveFailures == 1)
                        _log.LogError(ex, "Outbox dispatch cycle failed; backing off (next attempt in {Wait}s).", wait.TotalSeconds);
                    else
                        _log.LogWarning(
                            "Outbox dispatch still failing ({Failures} cycles): {Error}. Next attempt in {Wait}s.",
                            consecutiveFailures, ex.Message, wait.TotalSeconds);
                }

                try { await Task.Delay(wait, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _log.LogInformation("Outbox dispatcher stopped.");
        }

        /// <summary>
        /// One dispatch cycle: pull the dispatchable batch, then claim + deliver each
        /// row. Public so a test can drive a single deterministic cycle without the
        /// timer. Uses a fresh DI scope per cycle (the repos are scoped).
        /// </summary>
        public async Task PumpOnceAsync(CancellationToken ct = default)
        {
            using var scope = _scopes.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            var subs = scope.ServiceProvider.GetRequiredService<IWebhookSubscriptions>();
            var delivery = scope.ServiceProvider.GetRequiredService<IWebhookDelivery>();

            var now = DateTime.UtcNow;
            var batch = await outbox.GetDispatchableAsync(now, _opts.BatchSize, ct);
            if (batch.Count == 0) return;

            foreach (var row in batch)
            {
                if (ct.IsCancellationRequested) break;

                // Atomic claim: guard on the row's current status. If we lose the CAS,
                // another instance owns it — skip.
                if (!await outbox.TryClaimAsync(row, ct))
                    continue;

                await DeliverClaimedAsync(outbox, subs, delivery, row, ct);
            }
        }

        private async Task DeliverClaimedAsync(
            IOutbox outbox, IWebhookSubscriptions subs, IWebhookDelivery delivery, OutboxEvent row, CancellationToken ct)
        {
            // Claim bumped Attempts; work from the incremented count.
            var attempts = row.Attempts + 1;
            var envelope = EventEnvelope.From(row);

            bool delivered;
            try
            {
                delivered = await FanOutAsync(subs, delivery, row, envelope, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Delivery of event {Type} ({EventId}) threw; treating as a failed attempt.", row.Type, row.Id);
                delivered = false;
            }

            if (delivered)
            {
                await outbox.SaveAsync(row with
                {
                    Status = OutboxStatus.Dispatched,
                    Attempts = attempts,
                    DispatchedAt = DateTime.UtcNow,
                    NextAttemptAt = null,
                }, ct);
                _log.LogInformation("Event {Type} ({EventId}) seq {Seq} dispatched on attempt {Attempts}.",
                    row.Type, row.Id, row.Seq, attempts);
                return;
            }

            if (attempts >= _opts.MaxAttempts)
            {
                await outbox.SaveAsync(row with
                {
                    Status = OutboxStatus.Dead,
                    Attempts = attempts,
                    NextAttemptAt = null,
                }, ct);
                _log.LogError("Event {Type} ({EventId}) seq {Seq} is DEAD after {Attempts} attempts.",
                    row.Type, row.Id, row.Seq, attempts);
                return;
            }

            var next = DateTime.UtcNow.Add(Backoff(attempts));
            await outbox.SaveAsync(row with
            {
                Status = OutboxStatus.Failed,
                Attempts = attempts,
                NextAttemptAt = next,
            }, ct);
            _log.LogWarning("Event {Type} ({EventId}) seq {Seq} failed attempt {Attempts}; retry at {Next:o}.",
                row.Type, row.Id, row.Seq, attempts, next);
        }

        /// <summary>
        /// Deliver to every active subscription for the event's company whose Types
        /// match. "Delivered" means every matching webhook accepted it; a single
        /// failure fails the row so it retries (at-least-once — a receiver should be
        /// idempotent on X-AeroBus-Delivery). With no matching subscriptions the
        /// event is considered dispatched (nothing to deliver to).
        /// </summary>
        private static async Task<bool> FanOutAsync(
            IWebhookSubscriptions subs, IWebhookDelivery delivery, OutboxEvent row, EventEnvelope envelope, CancellationToken ct)
        {
            // Company-scoped events go to that company's webhooks; global (null
            // company) events currently have no webhook audience (subscriptions are
            // per-company) and are simply marked dispatched — still visible on SSE.
            if (row.CompanyId is not { } companyId) return true;

            var all = await subs.GetByCompanyAsync(companyId, ct);
            var matching = all
                .Where(s => s.Active && EventTypeMatch.MatchesAny(s.Types, row.Type))
                .ToList();
            if (matching.Count == 0) return true;

            var ok = true;
            foreach (var sub in matching)
                if (!await delivery.DeliverAsync(sub, envelope, ct))
                    ok = false;
            return ok;
        }

        /// <summary>Exponential backoff: Base × 2^(attempts-1), capped.</summary>
        private TimeSpan Backoff(int attempts)
        {
            var exp = Math.Pow(2, Math.Max(0, attempts - 1));
            var seconds = _opts.BackoffBaseSeconds * exp;
            var capped = Math.Min(seconds, _opts.BackoffCapSeconds);
            return TimeSpan.FromSeconds(capped);
        }
    }
}
