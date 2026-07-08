namespace AeroBus.Core.Model.Catalogue
{
    public sealed record FlightSolutionQuery(
        string From,
        string To,
        DateOnly DepartureDate,         // origin-local date
        int MaxStops = 1,               // 0 = direct only, 1 = allow single connection
        int MaxSolutions = 50,
        TimeSpan? LatestDepartureLocal = null,  // optional window end at origin
        TimeSpan? EarliestDepartureLocal = null // optional window start at origin
    );

    public sealed record FlightSolutionLeg(
        Guid FlightId,
        string From,
        string To,
        DateTime DepartureUtc,
        DateTime ArrivalUtc,
        DateTime DepartureLocal,
        DateTime ArrivalLocal
    );

    // Legs/TotalElapsed come from the positional parameters — do NOT redeclare them in the body,
    // or they shadow the constructor parameters and stay null/default when built positionally
    // (e.g. new FlightSolution(legs, total)). The extra descriptive fields below are optional.
    public sealed record FlightSolution(List<FlightSolutionLeg> Legs, TimeSpan TotalElapsed)
    {
        public string? Origin { get; set; }
        public string? Destination { get; set; }
        public DateTime DepartureUtc { get; set; }
        public DateTime DepartureLocal { get; set; }
        public DateTime ArrivalUtc { get; set; }
        public DateTime ArrivalLocal { get; set; }
    }
}
