using AeroBus.Core.Model.Customer;

namespace AeroBus.Core.Model.Distribution
{
    /// <summary>
    /// Order-create request. Ported from the ooms OrderCreateRequest, but adapted
    /// to the AeroBus offer/shop shape: ooms carried a fully-structured
    /// <c>Model.Offer.Offer</c> (with OfferItems/Services/FlightServices) in the
    /// request body — that OfferEngine type is permanently dropped in AeroBus. Here
    /// the caller instead references what was shopped by id:
    /// <list type="bullet">
    ///   <item><see cref="OfferId"/> — the persisted <c>offers</c> document (from /offer/shop).</item>
    ///   <item><see cref="SolutionId"/> — the chosen <see cref="OriginDestinationResponse"/>
    ///   flight solution within that offer.</item>
    ///   <item><see cref="BundleId"/> — the chosen <see cref="ShopBundle"/> on that solution.</item>
    /// </list>
    /// The service re-reads the offer document and binds the order against exactly
    /// what was shopped (never trusting client-supplied prices/flights).
    /// </summary>
    public sealed class OrderCreateRequest
    {
        public string Channel { get; set; } = string.Empty;

        /// <summary>The persisted offer (offers collection) to book against.</summary>
        public Guid OfferId { get; set; }

        /// <summary>The chosen flight solution within the offer (null → first solution found).</summary>
        public Guid? SolutionId { get; set; }

        /// <summary>The chosen priced bundle on that solution (null → cheapest priced bundle).</summary>
        public Guid? BundleId { get; set; }

        /// <summary>
        /// Multi-bound selection: one entry per origin-destination the caller is
        /// booking (e.g. outbound + return from a RETURN shop). Each entry picks a
        /// solution + bundle within <see cref="OfferId"/>; the order carries one
        /// order item per selection and inventory is secured across every leg of
        /// every selection atomically. When set, <see cref="SolutionId"/>/<see cref="BundleId"/>
        /// are ignored; when empty/null the legacy single-selection fields apply.
        /// </summary>
        public List<OrderSelection>? Selections { get; set; }

        public List<Passenger> Passengers { get; set; } = new();

        public PaymentRequest? Payment { get; set; }
    }

    /// <summary>One booked (solution, bundle) pick within the shopped offer.</summary>
    public sealed class OrderSelection
    {
        public Guid? SolutionId { get; set; }
        public Guid? BundleId { get; set; }
    }

    public sealed class PaymentRequest
    {
        public string Provider { get; set; } = "Manual";
        public string Method { get; set; } = "Card";
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
