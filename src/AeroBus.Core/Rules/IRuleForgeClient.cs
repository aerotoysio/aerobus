namespace AeroBus.Core.Rules
{
    /// <summary>
    /// Typed HTTP client for the external RuleForge rules engine. Sends the
    /// shared-secret <c>X-AERO-Key</c> header, POSTs a payload to a bound rule
    /// endpoint, and returns the parsed <see cref="RuleForgeEnvelope"/>.
    /// </summary>
    public interface IRuleForgeClient
    {
        /// <summary>
        /// Evaluate the rule bound to <paramref name="endpoint"/> against
        /// <paramref name="payload"/>. Appends <c>?debug=true</c> when
        /// <paramref name="debug"/> is set so the envelope carries a per-node
        /// trace. Throws on transport / non-success HTTP so the caller's
        /// <see cref="DecisionRunner"/> can apply the decision point's failure mode.
        /// </summary>
        Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default);

        /// <summary>Probe RuleForge's open <c>/health</c> endpoint.</summary>
        Task<bool> HealthAsync(CancellationToken ct = default);

        /// <summary>Ask RuleForge to drop its source caches and re-bind rules
        /// (<c>POST /admin/refresh</c>), e.g. after publishing a new version.</summary>
        Task<bool> RefreshAsync(CancellationToken ct = default);
    }
}
