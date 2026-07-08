namespace AeroBus.Core.Model.Shopping
{
    // Shop-facing projection of a catalogue flight solution. Ported (trimmed)
    // from the ooms distribution payload (Model.Distribution.OfferShop) — only
    // the flight-search shape survives here; the offer/bundle payload types stay
    // out of AeroBus (OfferEngine/Pricing are permanently dropped).

    public sealed class FlightSolution
    {
        public Guid Id { get; set; }
        public List<string>? PaxRefs { get; set; }
        public int ElapsedDurationMinutes { get; set; }
        public string? Cabin { get; set; }
        public List<FlightSegment>? Flights { get; set; }

        // Priced fare bundles for this solution, produced by the RuleForge
        // ShopBundles decision point. Added in Phase 4 (the offer/bundle payload
        // was previously kept out of the shop model): the shop response now
        // carries bundles alongside the flight search. Empty when RuleForge is
        // unavailable/degraded (see OfferShopResponse.Warnings).
        public List<Distribution.ShopBundle>? Bundles { get; set; }
    }

    public sealed class FlightSegment
    {
        public Guid Id { get; set; }
        public string? FlightRef { get; set; }

        public string? MarketingCarrier { get; set; }
        public string? MarketingFlightNumber { get; set; }

        public string? OperatingCarrier { get; set; }
        public string? OperatingFlightNumber { get; set; }

        public string? EquipmentCode { get; set; }
        public int SegmentDurationMinutes { get; set; }
        public string? BookingClass { get; set; }

        public FlightEndpoint? Departure { get; set; }
        public FlightEndpoint? Arrival { get; set; }
    }

    public sealed class FlightEndpoint
    {
        public string? Airport { get; set; }
        public string? Terminal { get; set; }
        public DateTime ScheduledTimeLocal { get; set; }
    }
}
