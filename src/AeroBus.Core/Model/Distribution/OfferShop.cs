using AeroBus.Core.Model.Shopping;

namespace AeroBus.Core.Model.Distribution
{
    // Ported from ooms Model.Distribution.OfferShop, keeping the /offer/shop
    // request/response contract. Trivial Request/Response base wrappers are
    // dropped; the fields they carried (Status/Warnings/ResponseTime) live
    // directly on OfferShopResponse. The flight-search shape reuses the existing
    // Model.Shopping.FlightSolution (now carrying priced Bundles) rather than
    // re-declaring a parallel type.

    // ─── request ────────────────────────────────────────────────────────────

    public sealed class OfferShopRequest
    {
        public SearchContext? SearchContext { get; set; }
        public List<OfferShopPassenger>? Passengers { get; set; }
        public SearchCriteria? SearchCriteria { get; set; }
    }

    public sealed class SearchContext
    {
        public string? Channel { get; set; }
        public string? PointOfSale { get; set; }
        public string? Currency { get; set; }
        public string? Locale { get; set; }
    }

    public sealed class OfferShopPassenger
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }   // ADT/CHD/INF etc
        public int Age { get; set; }
    }

    public sealed class SearchCriteria
    {
        public string? TripType { get; set; }   // ONE_WAY / RETURN / MULTICITY
        public List<OriginDestinationRequest>? OriginDestinations { get; set; }
        public List<string>? CabinPreferences { get; set; }
        public int? MaxConnections { get; set; } // null = default (allow 1); 0 = direct only; N = up to N stops
        public int MaxResultsPerOD { get; set; }
    }

    public sealed class OriginDestinationRequest
    {
        public string? OdRef { get; set; }
        public string? Origin { get; set; }
        public string? Destination { get; set; }
        public DateTime DepartureDate { get; set; }
    }

    // ─── response ───────────────────────────────────────────────────────────

    public sealed class OfferShopResponse
    {
        public Guid SearchId { get; set; }
        public string? Channel { get; set; }
        public string? Currency { get; set; }
        public List<OfferShopPassenger> Passengers { get; set; } = new();
        public List<OriginDestinationResponse> OriginDestinations { get; set; } = new();
        public PricingSummary PricingSummary { get; set; } = new();

        /// <summary>Populated when a RuleForge decision point degraded (e.g. the
        /// shop-bundles rule was unavailable): flight solutions still return, but
        /// carry empty bundles. The shop never 500s because RuleForge is down.</summary>
        public List<string> Warnings { get; set; } = new();

        public int ResponseTime { get; set; }
    }

    public sealed class OriginDestinationResponse
    {
        public Guid Id { get; set; }
        public string? OdRef { get; set; }
        public string? Origin { get; set; }
        public string? Destination { get; set; }
        public DateTime DepartureDate { get; set; }
        public List<FlightSolution> FlightSolutions { get; set; } = new();
    }

    // ─── bundles (fares/offers) ───────────────────────────────────────────────

    public sealed class ShopBundle
    {
        public Guid Id { get; set; }
        public string? BundleCode { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<Guid> EligiblePaxIds { get; set; } = new();
        public BundlePrice? Price { get; set; }
        public List<BundleServiceItem> Services { get; set; } = new();
    }

    public sealed class BundlePrice
    {
        public decimal Total { get; set; }
        public decimal Base { get; set; }
        public decimal Taxes { get; set; }
        public string? Currency { get; set; }
        public List<PriceComponent> Components { get; set; } = new();
    }

    public sealed class PriceComponent
    {
        public string? Code { get; set; }
        public string? Type { get; set; }     // BASE / TAX / etc
        public decimal Amount { get; set; }
    }

    public sealed class BundleServiceItem
    {
        public Guid Id { get; set; }
        public List<Guid> EligiblePaxIds { get; set; } = new();
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool Included { get; set; }
    }

    // ─── price summary ────────────────────────────────────────────────────────

    public sealed class PricingSummary
    {
        public decimal Total { get; set; }
        public string? Currency { get; set; }
        public List<PaxPriceSummary> PerPaxType { get; set; } = new();
    }

    public sealed class PaxPriceSummary
    {
        public string? Type { get; set; }     // ADT, CHD, INF
        public int Count { get; set; }
        public decimal Total { get; set; }
    }
}
