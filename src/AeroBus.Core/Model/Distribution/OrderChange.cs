using AeroBus.Core.Model.Order;

namespace AeroBus.Core.Model.Distribution
{
    // Order status-change contract. Ported from ooms Model.Distribution.OrderChange.
    // Request/Response base wrappers dropped; status fields live directly on the
    // response (mirroring the Phase 4 offer models).

    public sealed class OrderChangeRequest
    {
        public Guid OrderId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public sealed class OrderChangeResponse
    {
        public Guid OrderId { get; set; }
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }

        public string PreviousStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public List<string> AvailableActions { get; set; } = new();
        public List<OrderHistory> History { get; set; } = new();

        /// <summary>Set when a Cancel/Refund released seat inventory back to the pool.</summary>
        public bool InventoryReleased { get; set; }
    }
}
