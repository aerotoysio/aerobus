using AeroBus.Core.Model;

namespace AeroBus.Core.Model.Operations
{
    /// <summary>
    /// One passenger's operational (departure-control) state for one flight — the
    /// row a check-in/gate agent works. Split out of the order document (which is
    /// the commercial record) for the same reasons <c>FlightInventory</c> was split
    /// out of <c>Flight</c>: the keys and status are <b>top-level scalars</b>, so the
    /// collection is queryable by flight (the manifest) and updatable through the
    /// DocumentForge conditional-update (compare-and-set) primitive without racing.
    ///
    /// Populated at booking time (one row per passenger per flight service, status
    /// <see cref="CheckInStatus.Booked"/>) and advanced NotBooked→CheckedIn→Boarded
    /// by the DCS surface. Name/PaxType/BookedBucket are denormalised so a manifest
    /// renders without re-reading every order.
    /// </summary>
    public sealed record PassengerCheckIn : IDocument
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        // Top-level keys — queryable + CAS-targetable.
        public Guid FlightId { get; set; }
        public Guid OrderId { get; set; }
        public Guid PassengerId { get; set; }

        // Denormalised passenger identity for the manifest.
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PaxType { get; set; } = string.Empty;
        public string? BookedBucket { get; set; }

        // Operational state.
        public string Status { get; set; } = CheckInStatus.Booked;
        public int? SeatRow { get; set; }
        public string? SeatColumn { get; set; }
        public int? BoardingSequence { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public DateTime? BoardedAt { get; set; }

        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
    }

    /// <summary>The per-passenger operational lifecycle: Booked → CheckedIn → Boarded.</summary>
    public static class CheckInStatus
    {
        public const string Booked = "Booked";
        public const string CheckedIn = "CheckedIn";
        public const string Boarded = "Boarded";
    }
}
