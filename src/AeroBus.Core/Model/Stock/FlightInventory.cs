namespace AeroBus.Core.Model.Stock
{
    /// <summary>
    /// Per-flight, per-bucket seat inventory. One document per (flight, bucket)
    /// in the <c>flightinventory</c> collection — its own collection (not embedded
    /// in Flight) because it is the high-write transactional record.
    ///
    /// ALL counters are top-level scalars by design: the DocumentForge
    /// conditional-update primitive (compare-and-swap on a field) can only
    /// target top-level fields, and sell/refund flows will use it against
    /// <see cref="Sold"/>/<see cref="Available"/>.
    ///
    /// Bucket is the layout compartment code (e.g. "Y", "J"), or "ALL" when the
    /// flight has no layout and capacity comes from the schedule/flight total.
    /// </summary>
    public sealed record FlightInventory : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public Guid FlightId { get; init; }
        public string Bucket { get; init; } = string.Empty;
        public int Capacity { get; init; }
        public int Sold { get; init; }
        public int Available { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public string? Status { get; init; }
    }
}
