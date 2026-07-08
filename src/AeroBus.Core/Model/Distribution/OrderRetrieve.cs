using AeroBus.Core.Model.Customer;
using AeroBus.Core.Model.Order;

namespace AeroBus.Core.Model.Distribution
{
    // Order retrieve/view contract. Ported from ooms Model.Distribution.OrderRetrieve.
    // The ooms Request/Response base wrappers are dropped (as Phase 4 did for the
    // offer models): the fields they carried (Status/ErrorMessage) live directly on
    // the response types. The GitHub "Octokit" using in the ooms file was a stray
    // import and is not carried over.

    public sealed class OrderRetrieveRequest
    {
        public Guid Id { get; set; }
        public string? OrderId { get; set; }
        public string? LastName { get; set; }
    }

    public sealed class OrderListRequest
    {
        public Guid CustomerId { get; set; }
    }

    public sealed class OrderViewResponse
    {
        /// <summary>Empty on error (see <see cref="ErrorMessage"/>).</summary>
        public List<OrderView> Orders { get; set; } = new();

        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public int ResponseTime { get; set; }
    }

    public sealed class OrderView
    {
        public Order.Order? Order { get; set; }
        public List<Passenger> Passengers { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();
        public List<OrderItemCharge> Charges { get; set; } = new();
    }
}
