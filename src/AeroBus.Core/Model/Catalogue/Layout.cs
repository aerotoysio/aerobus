namespace AeroBus.Core.Model.Catalogue
{
    // Aircraft cabin layout — one DocumentForge document. Compartments embed their
    // seats; seat types are embedded. Intra-aggregate refs (Seat.SeatTypeId,
    // LayoutCompartment.DefaultSeatTypeId) are kept; parent/self FKs and child
    // tenant/concurrency fields are dropped (the root owns those).

    public sealed record Layout
    {
        public Guid Id { get; init; }
        public string? Type { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public DateTime? StartDate { get; init; }
        public DateTime? EndDate { get; init; }
        public string? Data { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? ConcurrencyId { get; init; }
        public Guid? EquipmentId { get; init; }   // cross-aggregate → Equipment
        public Guid? CompanyId { get; init; }

        public List<LayoutCompartment>? Compartments { get; set; }
        public List<SeatType>? SeatTypes { get; set; }
    }

    public sealed record LayoutCompartment
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Code { get; init; }
        public int? StartRow { get; init; }
        public int? EndRow { get; init; }
        public string? Columns { get; init; }
        public string? Status { get; init; }
        public int? Level { get; init; }
        public int? Order { get; init; }
        public int? StockCapacity { get; init; }
        public Guid? DefaultSeatTypeId { get; init; }  // intra-aggregate → SeatType
        public int? FrontOffset { get; init; }
        public int? RearOffset { get; init; }

        public List<Seat>? Seats { get; set; }
    }

    public sealed record Seat
    {
        public Guid Id { get; init; }
        public int? RowNumber { get; init; }
        public string? Column { get; init; }
        public string? Status { get; init; }
        public Guid? SeatTypeId { get; init; }  // intra-aggregate → SeatType
    }

    public sealed record SeatType
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Status { get; init; }
    }
}
