using AeroBus.Core.Model;
using AeroBus.Core.Model.Shopping;

namespace AeroBus.Core.Model.Distribution
{
    /// <summary>
    /// A shopped offer, persisted to the <c>offers</c> collection so a later
    /// order-create can re-price / bind against exactly what was shopped. Holds a
    /// snapshot of the solutions + bundles the shop returned, plus the RuleForge
    /// rule id/version that produced them. Expires (default now+30min) — the
    /// Redis offer cache from ooms is intentionally NOT ported; DocumentForge is
    /// the single store.
    /// </summary>
    public sealed class Offer : IDocument
    {
        public Guid Id { get; set; }
        public Guid? CompanyId { get; set; }

        /// <summary>The shop response's SearchId this offer belongs to.</summary>
        public Guid SearchId { get; set; }

        public string? Channel { get; set; }
        public string? Currency { get; set; }

        public List<OfferShopPassenger> Passengers { get; set; } = new();

        /// <summary>Snapshot of the shopped solutions + bundles (O&amp;D grouped).</summary>
        public List<OriginDestinationResponse> OriginDestinations { get; set; } = new();

        /// <summary>Convenience denormalisation: cheapest-bundle-per-solution total.</summary>
        public PricingSummary PricingSummary { get; set; } = new();

        /// <summary>RuleForge rule that priced the bundles (null when degraded).</summary>
        public string? RuleId { get; set; }
        public int? RuleVersion { get; set; }

        public DateTime? ExpiresAt { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
        public string? Status { get; set; }
    }
}
