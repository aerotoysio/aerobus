using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Schedule
    {
        public Guid Id { get; init; }
        public Guid CompanyId { get; init; }
        public Guid? GroupingId { get; init; }
        public Guid LayoutId { get; init; }
        public string CarrierCode { get; init; } = string.Empty;
        public string FlightNumber { get; init; } = string.Empty;
        public string DepartureStation { get; init; } = string.Empty;
        public string ArrivalStation { get; init; } = string.Empty;
        public TimeSpan DepartureTimeLocal { get; init; }
        public TimeSpan ArrivalTimeLocal { get; init; }
        public int ArrivalOffsetDays { get; init; }
        public DateTime StartDateLocal { get; init; }
        public DateTime EndDateLocal { get; init; }
        public bool Monday { get; init; }
        public bool Tuesday { get; init; }
        public bool Wednesday { get; init; }
        public bool Thursday { get; init; }
        public bool Friday { get; init; }
        public bool Saturday { get; init; }
        public bool Sunday { get; init; }
        public string? Tags { get; init; }
        public JsonNode? Data { get; init; }
        public DateTime? Updated { get; init; }
        public DateTime Created { get; init; }
        public string? Status { get; set; }
        public Guid ConcurrencyId { get; init; }
        public string? MarketingCarrier { get; init; }
        public string? OperatingCarrier { get; init; }
        public string? EquipmentCode { get; init; }
        public decimal? CostAmount { get; init; }              // schedule-level default; null = no cost data
        public string? CostCurrency { get; init; }             // 3-char ISO; null inherits Company.OperatingCurrency at runtime
        public decimal? ExpectedLoadFactor { get; init; }      // 0..1; null inherits Company.DefaultExpectedLoadFactor
        public int? Capacity { get; init; }                    // total seats
        public string? DepartureTerminal { get; init; }
        public string? ArrivalTerminal { get; init; }
    }
}
