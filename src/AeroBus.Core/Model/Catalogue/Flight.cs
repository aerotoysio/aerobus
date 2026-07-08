using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Flight
    {
        public Guid Id { get; init; }
        public Guid ScheduleId { get; set; }
        public Guid CompanyId { get; init; }
        public Guid LayoutId { get; init; }
        public string DepartureStation { get; init; } = string.Empty;
        public string ArrivalStation { get; init; } = string.Empty;
        public DateTime DepartureDateTimeLocal { get; init; }
        public DateTime ArrivalDateTimeLocal { get; init; }
        public DateTime DepartureDateTime { get; init; }
        public DateTime ArrivalDateTime { get; init; }
        public JsonNode? Data { get; init; }
        public string? Tags { get; init; }
        public DateTime? Updated { get; init; }
        public DateTime Created { get; init; }
        public string? Status { get; init; }
        public Guid ConcurrencyId { get; init; }
        public string? MarketingCarrier { get; init; }
        public string? OperatingCarrier { get; init; }
        public string? FlightNumber { get; init; }
        public string? EquipmentCode { get; init; }
        public string? DepartureTerminal { get; init; }
        public string? ArrivalTerminal { get; init; }
        public int? BlockMinutes { get; init; }
        public int? DistanceNm { get; init; }
        public byte? OnTimePct { get; init; }
        public byte? CancelPct { get; init; }
        public decimal? CostAmount { get; init; }              // per-flight override of Schedule.CostAmount (copy-on-create)
        public string? CostCurrency { get; init; }             // per-flight override of Schedule.CostCurrency
        public decimal? ExpectedLoadFactor { get; init; }      // per-flight override of Schedule.ExpectedLoadFactor
        public int? Capacity { get; init; }                    // per-flight override of Schedule.Capacity

        // Display denormalisation of the per-bucket FlightInventory documents.
        // The flightinventory collection is the transactional source of truth
        // (top-level counters, conditional-update friendly); these totals ride
        // on the flight document for cheap list/detail rendering.
        public FlightCounters? Counters { get; set; }
    }

    public sealed record FlightCounters
    {
        public int? Capacity { get; init; }
        public int? Sold { get; init; }
        public int? Available { get; init; }
    }
}
