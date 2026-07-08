using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Rules
{
    /// <summary>
    /// The outcome of running a decision point. When RuleForge applied a rule,
    /// <see cref="Envelope"/> carries its verdict/result. When it was
    /// unavailable/errored/skipped, <see cref="Envelope"/> may be null and
    /// <see cref="Degraded"/> is set with a human-readable <see cref="Warning"/>.
    /// </summary>
    public sealed record DecisionOutcome(RuleForgeEnvelope? Envelope, bool Degraded, string? Warning = null)
    {
        /// <summary>True when RuleForge returned a rule that applied (a usable result).</summary>
        public bool Applied => Envelope is { Decision: Decision.Apply };
    }

    /// <summary>
    /// Per-decision-point failure behaviour. Values may be set from the
    /// "RuleForge:FailureModes" config section keyed by <see cref="DecisionPoint"/>
    /// name; unset points fall back to <see cref="DecisionRunner"/>'s built-in
    /// defaults (ShopBundles → Degrade, order points → Allow).
    /// </summary>
    public sealed class DecisionRunnerOptions
    {
        public Dictionary<string, FailureMode> FailureModes { get; set; } = new();
    }

    /// <summary>
    /// Evaluates named decision points through RuleForge and applies a per-point
    /// failure mode when the engine times out, errors, or returns
    /// <see cref="Decision.Error"/>. Local domain checks (Phase 5) remain the hard
    /// gate for order points, so their default mode is <see cref="FailureMode.Allow"/>;
    /// shopping defaults to <see cref="FailureMode.Degrade"/> so /offer/shop never
    /// 500s just because RuleForge is down.
    /// </summary>
    public sealed class DecisionRunner
    {
        private static readonly IReadOnlyDictionary<DecisionPoint, FailureMode> Defaults =
            new Dictionary<DecisionPoint, FailureMode>
            {
                [DecisionPoint.ShopBundles] = FailureMode.Degrade,
                [DecisionPoint.OfferPricing] = FailureMode.Degrade,
                [DecisionPoint.OrderValidate] = FailureMode.Allow,
                [DecisionPoint.OrderChangeEligibility] = FailureMode.Allow,
                [DecisionPoint.RefundEligibility] = FailureMode.Allow,
            };

        private readonly IRuleForgeClient _client;
        private readonly RuleForgeOptions _options;
        private readonly ConcurrentDictionary<DecisionPoint, FailureMode> _failureModes;
        private readonly ILogger<DecisionRunner> _log;

        public DecisionRunner(
            IRuleForgeClient client,
            IOptions<RuleForgeOptions> options,
            IOptions<DecisionRunnerOptions> runnerOptions,
            ILogger<DecisionRunner> log)
        {
            _client = client;
            _options = options.Value;
            _log = log;
            _failureModes = new ConcurrentDictionary<DecisionPoint, FailureMode>();
            foreach (var kv in runnerOptions.Value.FailureModes)
                if (Enum.TryParse<DecisionPoint>(kv.Key, ignoreCase: true, out var point))
                    _failureModes[point] = kv.Value;
        }

        /// <summary>Resolve the effective failure mode for a point (config override or built-in default).</summary>
        public FailureMode FailureModeFor(DecisionPoint point) =>
            _failureModes.TryGetValue(point, out var mode) ? mode : Defaults.GetValueOrDefault(point, FailureMode.Deny);

        /// <summary>
        /// Run <paramref name="point"/> against <paramref name="payload"/>. Never
        /// throws for RuleForge unavailability — a timeout / HTTP error /
        /// <see cref="Decision.Error"/> is mapped to the point's failure mode and
        /// returned as a (possibly degraded) <see cref="DecisionOutcome"/>. Every
        /// non-apply outcome is logged with the ruleId/version and reason.
        /// </summary>
        public async Task<DecisionOutcome> RunAsync(DecisionPoint point, object payload, bool debug = false, CancellationToken ct = default)
        {
            var endpoint = _options.Endpoints.For(point);
            var mode = FailureModeFor(point);

            RuleForgeEnvelope envelope;
            try
            {
                envelope = await _client.EvaluateAsync(endpoint, payload, debug, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Caller cancelled — propagate, this is not a RuleForge failure.
                throw;
            }
            catch (Exception ex)
            {
                // Timeout (TaskCanceledException) or transport / non-success HTTP.
                var reason = ex is TaskCanceledException ? "timeout" : ex.Message;
                return ApplyFailureMode(point, mode, endpoint, reason);
            }

            return envelope.Decision switch
            {
                Decision.Apply => new DecisionOutcome(envelope, Degraded: false),
                Decision.Skip => LogAndReturn(
                    new DecisionOutcome(envelope, Degraded: false),
                    point, endpoint, envelope, "rule skipped (did not apply)"),
                Decision.Error => ApplyFailureMode(point, mode, endpoint, "rule returned decision=error", envelope),
                _ => ApplyFailureMode(point, mode, endpoint, $"unknown decision '{envelope.Decision}'", envelope),
            };
        }

        private DecisionOutcome ApplyFailureMode(
            DecisionPoint point, FailureMode mode, string endpoint, string reason, RuleForgeEnvelope? envelope = null)
        {
            var ruleId = envelope?.RuleId ?? "(none)";
            var version = envelope?.RuleVersion;
            _log.LogWarning(
                "RuleForge decision point {Point} ({Endpoint}) not applied: {Reason}. rule={RuleId}@{Version}, failureMode={Mode}",
                point, endpoint, reason, ruleId, version, mode);

            return mode switch
            {
                // Degrade / Allow both proceed without a rule result; the caller
                // decides what "proceed" means for that point. They differ only in
                // intent (degrade = missing enrichment, allow = soft yes) and both
                // surface a warning. Deny signals a hard "no".
                FailureMode.Degrade => new DecisionOutcome(envelope, Degraded: true, Warning: $"{point}: {reason}"),
                FailureMode.Allow => new DecisionOutcome(envelope, Degraded: true, Warning: $"{point}: {reason} (allowed by failure mode)"),
                FailureMode.Deny => new DecisionOutcome(envelope, Degraded: true, Warning: $"{point}: {reason} (denied by failure mode)"),
                _ => new DecisionOutcome(envelope, Degraded: true, Warning: $"{point}: {reason}"),
            };
        }

        private DecisionOutcome LogAndReturn(
            DecisionOutcome outcome, DecisionPoint point, string endpoint, RuleForgeEnvelope envelope, string reason)
        {
            _log.LogInformation(
                "RuleForge decision point {Point} ({Endpoint}): {Reason}. rule={RuleId}@{Version}",
                point, endpoint, reason, envelope.RuleId, envelope.RuleVersion);
            return outcome;
        }
    }
}
