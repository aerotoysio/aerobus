namespace AeroBus.Core.Model.Catalogue
{
    /// <summary>
    /// One include/exclude rule embedded in a <see cref="MarketZone"/>. References a
    /// location (Continent/Country/Region/Airport) by id; Build resolves it to airports.
    /// Parent FK (MarketZoneId) and tenant/concurrency fields are dropped — the zone owns those.
    /// </summary>
    public sealed record MarketZoneSelector
    {
        public Guid Id { get; init; }
        public string? LocationType { get; init; }   // Continent / Country / Region / Airport
        public Guid? LocationId { get; init; }
        public bool? Included { get; init; }          // null/true = include, false = exclude
    }
}
