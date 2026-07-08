namespace AeroBus.Core.Rules
{
    /// <summary>
    /// Configuration for the RuleForge integration, bound from the "RuleForge"
    /// configuration section. RuleForge is the external ASP.NET rules engine
    /// AeroBus calls at named decision points (shop bundles, offer pricing,
    /// order validation, etc.) — see <see cref="DecisionPoint"/>.
    /// </summary>
    public sealed class RuleForgeOptions
    {
        /// <summary>Config section name.</summary>
        public const string SectionName = "RuleForge";

        /// <summary>Base URL of the RuleForge HTTP service (dev default port 5050).</summary>
        public string BaseUrl { get; set; } = "http://localhost:5050";

        /// <summary>
        /// Shared secret sent as the <c>X-AERO-Key</c> header on every request.
        /// Empty disables the header (RuleForge allows anonymous traffic when it
        /// has no key configured — the local-dev default).
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Per-request timeout in milliseconds. Kept short — RuleForge's
        /// warm hot path is sub-millisecond, so a slow response means a problem
        /// and the caller should fall back rather than block the shop.</summary>
        public int TimeoutMs { get; set; } = 2000;

        /// <summary>
        /// Named decision-point endpoints. Defaults match the RuleForge routes
        /// AeroBus binds rules to; override per environment if the rule authors
        /// bind different paths.
        /// </summary>
        public RuleForgeEndpoints Endpoints { get; set; } = new();
    }

    /// <summary>The RuleForge endpoint path for each named decision point.</summary>
    public sealed class RuleForgeEndpoints
    {
        public string ShopBundles { get; set; } = "/v1/offer/shop-bundles";
        public string OfferPricing { get; set; } = "/v1/offer/price";
        public string OrderValidate { get; set; } = "/v1/order/validate";
        public string OrderChangeEligibility { get; set; } = "/v1/order/change-eligibility";
        public string RefundEligibility { get; set; } = "/v1/order/refund-eligibility";

        /// <summary>Resolve the configured path for a <see cref="DecisionPoint"/>.</summary>
        public string For(DecisionPoint point) => point switch
        {
            DecisionPoint.ShopBundles => ShopBundles,
            DecisionPoint.OfferPricing => OfferPricing,
            DecisionPoint.OrderValidate => OrderValidate,
            DecisionPoint.OrderChangeEligibility => OrderChangeEligibility,
            DecisionPoint.RefundEligibility => RefundEligibility,
            _ => throw new ArgumentOutOfRangeException(nameof(point), point, "Unknown decision point."),
        };
    }

    /// <summary>
    /// The named decision points AeroBus evaluates through RuleForge. Order
    /// points arrive in Phase 5; only <see cref="ShopBundles"/> and
    /// <see cref="OfferPricing"/> are wired to endpoints in Phase 4.
    /// </summary>
    public enum DecisionPoint
    {
        ShopBundles,
        OfferPricing,
        OrderValidate,
        OrderChangeEligibility,
        RefundEligibility,
    }

    /// <summary>
    /// What a decision point does when RuleForge is unavailable or errors
    /// (timeout / HTTP failure / <see cref="Decision.Error"/>).
    /// <list type="bullet">
    /// <item><see cref="Degrade"/> — proceed without the rule's result, flagged degraded (shop bundles).</item>
    /// <item><see cref="Deny"/> — treat as a hard "no" for the decision.</item>
    /// <item><see cref="Allow"/> — treat as a soft "yes"; local checks remain the gate (order points).</item>
    /// </list>
    /// </summary>
    public enum FailureMode
    {
        Degrade,
        Deny,
        Allow,
    }
}
