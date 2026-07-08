namespace AeroBus.Core.Model.Catalogue
{
    // A custom market: a set of include/exclude location rules (Selectors) that
    // Build compiles into a flat IncludedAirports list. Selectors are embedded —
    // the zone is one document.
    public sealed record MarketZone : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }

        public string? Name { get; init; }
        public string? Description { get; init; }

        public int IncludedAirportCount { get; init; }   // materialised by Build
        public string? IncludedAirports { get; init; }    // CSV of IATA codes, materialised by Build

        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }

        // Include/exclude rules over Continent/Country/Region/Airport. Build()
        // resolves these against the geo data into IncludedAirports.
        public List<MarketZoneSelector>? Selectors { get; set; }
    }
}
