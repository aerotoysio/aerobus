using AeroBus.Core.Model;

namespace AeroBus.Core.Model.Order
{
    // The Order is a single DocumentForge document (aggregate root). Children are
    // embedded, so they do NOT carry parent/self foreign keys (OrderId,
    // OrderItemId, ServiceId, …) — only cross-aggregate references that point at
    // *other* documents are kept (Service.PassengerId → Customer/Passenger,
    // FlightService.FlightId → Flight, Service.ProductId → Product).
    //
    // Ported from ooms Model.Order.Order. Order implements IDocument (Guid Id) and
    // carries CompanyId for tenant scoping; the embedded children have only their
    // own ids.

    public sealed record Order : IDocument
    {
        public Guid Id { get; init; }
        public string OrderId { get; set; } = string.Empty;
        public int OrderSequence { get; init; }
        public Guid? ProfileId { get; init; }
        public string? Channel { get; init; }
        public string? Type { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? ConcurrencyId { get; init; }
        public Guid? CompanyId { get; init; }

        public List<OrderItem>? OrderItems { get; set; }
        public List<Payment>? Payments { get; set; }
        public List<OrderHistory>? History { get; set; }
        public List<Metadata>? Metadata { get; set; }
        // Passengers are booking-scoped — embedded in the order document.
        public List<Customer.Passenger>? Passengers { get; set; }
    }

    public sealed record OrderItem
    {
        public Guid Id { get; init; }
        public string? Type { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public decimal? Amount { get; init; }
        public string? Currency { get; init; }
        public string? Status { get; set; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }

        public List<Service>? Services { get; set; }
        public List<OrderItemCharge>? Charges { get; set; }
    }

    public sealed record Service
    {
        public Guid Id { get; init; }
        public Guid? ProductId { get; init; }    // cross-aggregate → Product
        public Guid? PassengerId { get; init; }  // cross-aggregate → Customer/Passenger
        public string? Type { get; init; }
        public string? Status { get; set; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }

        public List<FlightService>? FlightServices { get; set; }
        public List<AncillaryService>? AncillaryServices { get; set; }
    }

    public sealed record OrderItemCharge
    {
        public Guid Id { get; init; }
        public string AmountType { get; init; } = string.Empty;
        public string? Code { get; init; }
        public string Currency { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string? Jurisdiction { get; init; }
        public string Status { get; init; } = "Active";
        public DateTime Created { get; init; }
        public DateTime Updated { get; init; }
    }

    public sealed record FlightService
    {
        public Guid Id { get; init; }
        public Guid? FlightId { get; init; }     // cross-aggregate → Flight
        // Seat inventory bucket this flight service draws from (layout compartment
        // code, e.g. "Y"/"J", or "ALL"). Sell/release target the matching
        // flightinventory (FlightId, Bucket) document.
        public string? Bucket { get; init; }
        public int? SeatRow { get; init; }
        public string? SeatColumn { get; init; }
        public string? Status { get; set; }
        public List<Catalogue.Flight>? Flights { get; set; }
    }

    public sealed record AncillaryService
    {
        public Guid Id { get; init; }
        public string? AncillaryCode { get; init; }
        public int? Quantity { get; init; }
        public List<Catalogue.Product>? Products { get; set; }
    }

    public sealed record Payment
    {
        public Guid Id { get; init; }
        public string? Provider { get; init; }
        public string? Method { get; init; }
        public string? Currency { get; init; }
        public decimal? AuthorizedAmount { get; init; }
        public decimal? CapturedAmount { get; init; }
        public decimal? RefundedAmount { get; init; }
        public string? ProviderRef { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? ProfileId { get; init; }
        public decimal? SettledAmount { get; init; }
    }

    public sealed record OrderHistory
    {
        public Guid Id { get; init; }
        public string FromStatus { get; init; } = string.Empty;
        public string ToStatus { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public Guid? ActorId { get; init; }
        public DateTime Created { get; init; }
    }

    public sealed record Metadata
    {
        public Guid Id { get; init; }
        public Guid? ConcurrencyId { get; init; }
        public string? ParentType { get; init; }
        public Guid? ParentId { get; init; }
        public string? DataKey { get; init; }
        public string? DataName { get; init; }
        public string? DataType { get; init; }
        public string? DataValue { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
