using AeroBus.Core.Data;
using AeroBus.Core.Events;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Model.Order;
using AeroBus.Core.Repositories.Order;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Stock;
using Microsoft.Extensions.Logging;
using OrderModel = AeroBus.Core.Model.Order.Order;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>
    /// Order status-change runtime. Ported from the ooms OrderChangeService with
    /// two Phase-5 additions:
    /// <list type="number">
    ///   <item>RuleForge policy hooks — <see cref="DecisionPoint.OrderChangeEligibility"/>
    ///   on any change and <see cref="DecisionPoint.RefundEligibility"/> on a refund
    ///   (best-effort, default-Allow; the state machine below is still authoritative); and</item>
    ///   <item>seat-inventory RELEASE on a Cancel transition, returning the order's
    ///   seats to the pool. Release is naturally idempotent: the state machine only
    ///   allows Cancel once (from Pending/Confirmed/Fulfilled → Cancelled), so a
    ///   re-cancel fails the transition before any second release can run.</item>
    /// </list>
    ///
    /// The ooms TenantBus.Send publish is replaced by an events-outbox comment
    /// (Phase 6).
    /// </summary>
    public sealed class OrderChangeService
    {
        private readonly IOrders _orders;
        private readonly IInventoryService _inventory;
        private readonly DecisionRunner _decisions;
        private readonly IEventPublisher _events;
        private readonly ILogger<OrderChangeService> _log;

        public OrderChangeService(
            IOrders orders,
            IInventoryService inventory,
            DecisionRunner decisions,
            IEventPublisher events,
            ILogger<OrderChangeService> log)
        {
            _orders = orders;
            _inventory = inventory;
            _decisions = decisions;
            _events = events;
            _log = log;
        }

        public async Task<OrderChangeResponse> ChangeStatus(OrderChangeRequest request, Guid companyId, bool debug = false, CancellationToken ct = default)
        {
            var response = new OrderChangeResponse { OrderId = request.OrderId };

            var order = await _orders.GetByIdAsync(request.OrderId, ct);
            if (order is null || order.CompanyId != companyId)
            {
                response.Success = false;
                response.ErrorMessage = "Order not found.";
                return response;
            }

            var currentStatus = order.Status ?? OrderStateMachine.Status.Pending;
            response.PreviousStatus = currentStatus;

            // ── state machine (hard gate) ─────────────────────────────────────
            var newStatus = OrderStateMachine.TryTransition(currentStatus, request.Action);
            if (newStatus is null)
            {
                response.Success = false;
                response.ErrorMessage = $"Cannot perform '{request.Action}' on order in '{currentStatus}' status.";
                response.AvailableActions = OrderStateMachine.GetAvailableActions(currentStatus).ToList();
                return response;
            }

            // ── RuleForge eligibility (best-effort policy hooks) ──────────────
            // Default failure mode Allow: a RuleForge outage must never block a
            // legitimate change/refund. The state machine already gated above.
            var changeCheck = await _decisions.RunAsync(DecisionPoint.OrderChangeEligibility, new
            {
                orderId = order.Id,
                orderRef = order.OrderId,
                fromStatus = currentStatus,
                action = request.Action,
                toStatus = newStatus,
            }, debug, ct);
            if (IsDenied(changeCheck))
            {
                response.Success = false;
                response.ErrorMessage = changeCheck.Warning ?? "Change denied by policy.";
                response.AvailableActions = OrderStateMachine.GetAvailableActions(currentStatus).ToList();
                return response;
            }

            if (request.Action == OrderStateMachine.Action.Refund)
            {
                var refundCheck = await _decisions.RunAsync(DecisionPoint.RefundEligibility, new
                {
                    orderId = order.Id,
                    orderRef = order.OrderId,
                    fromStatus = currentStatus,
                }, debug, ct);
                if (IsDenied(refundCheck))
                {
                    response.Success = false;
                    response.ErrorMessage = refundCheck.Warning ?? "Refund denied by policy.";
                    response.AvailableActions = OrderStateMachine.GetAvailableActions(currentStatus).ToList();
                    return response;
                }
            }

            // ── inventory release on cancel ───────────────────────────────────
            // Return every booked seat to the pool. This is the only transition
            // that touches inventory, and it can only fire once per order.
            if (request.Action == OrderStateMachine.Action.Cancel)
            {
                await ReleaseOrderInventoryAsync(order, ct);
                response.InventoryReleased = true;
            }

            // ── apply + persist ───────────────────────────────────────────────
            var history = order.History ?? new List<OrderHistory>();
            history.Add(new OrderHistory
            {
                Id = Guid.NewGuid(),
                FromStatus = currentStatus,
                ToStatus = newStatus,
                Action = request.Action,
                Reason = request.Reason,
                Created = DateTime.UtcNow,
            });

            var updated = order with
            {
                Status = newStatus,
                Updated = DateTime.UtcNow,
                History = history,
            };
            MarkItemsStatus(updated, newStatus);

            await _orders.SaveAsync(updated, ct);

            // A Cancel emits the specific order.cancelled; every other transition
            // emits the generic order.changed. Both carry the from/to status so a
            // subscriber can react without re-fetching the order.
            var eventType = request.Action == OrderStateMachine.Action.Cancel
                ? "order.cancelled"
                : "order.changed";
            await _events.PublishAsync(eventType,
                new EventSubject(DfCollections.Order.Orders, updated.Id.ToString()),
                new
                {
                    orderId = updated.OrderId,
                    id = updated.Id,
                    action = request.Action,
                    fromStatus = currentStatus,
                    toStatus = newStatus,
                    reason = request.Reason,
                },
                updated.CompanyId, actor: "order-change", ct);

            response.NewStatus = newStatus;
            response.Action = request.Action;
            response.AvailableActions = OrderStateMachine.GetAvailableActions(newStatus).ToList();
            response.History = history;

            return response;
        }

        private async Task ReleaseOrderInventoryAsync(OrderModel order, CancellationToken ct)
        {
            var companyId = order.CompanyId ?? Guid.Empty;
            var seats = order.Passengers?.Count ?? 0;
            if (seats == 0 || companyId == Guid.Empty) return;

            var legs = (order.OrderItems ?? new List<OrderItem>())
                .SelectMany(i => i.Services ?? new List<Service>())
                .SelectMany(s => s.FlightServices ?? new List<FlightService>())
                .Where(fs => fs.FlightId is { } fid && fid != Guid.Empty)
                .Select(fs => (FlightId: fs.FlightId!.Value, Bucket: string.IsNullOrWhiteSpace(fs.Bucket) ? "ALL" : fs.Bucket!))
                .Distinct()
                .ToList();

            foreach (var (flightId, bucket) in legs)
            {
                try
                {
                    var result = await _inventory.ReleaseAsync(companyId, flightId, bucket, seats, ct);
                    if (result.Success)
                        await _events.PublishAsync("inventory.adjusted",
                            new EventSubject(DfCollections.Stock.FlightInventory, flightId.ToString()),
                            new { flightId, bucket, delta = seats, reason = "order.cancel" },
                            companyId, actor: "order-change", ct);
                    else
                        _log.LogWarning(
                            "Inventory release on cancel for order {OrderRef} flight {FlightId}/{Bucket} returned {Reason}.",
                            order.OrderId, flightId, bucket, result.Reason);
                }
                catch (Exception ex)
                {
                    // Never let a release error abort the cancel — the order must
                    // still move to Cancelled. Log for the operator.
                    _log.LogError(ex,
                        "Inventory release on cancel failed for order {OrderRef} flight {FlightId}/{Bucket}.",
                        order.OrderId, flightId, bucket);
                }
            }
        }

        private static void MarkItemsStatus(OrderModel order, string status)
        {
            // Only mirror terminal statuses onto items/services; the in-flight
            // statuses (CheckedIn/Boarded/…) are order-level lifecycle, not item state.
            if (status is not (OrderStateMachine.Status.Cancelled or OrderStateMachine.Status.Refunded)) return;
            foreach (var item in order.OrderItems ?? new List<OrderItem>())
            {
                item.Status = status;
                foreach (var svc in item.Services ?? new List<Service>())
                {
                    svc.Status = status;
                    foreach (var fs in svc.FlightServices ?? new List<FlightService>())
                        fs.Status = status;
                }
            }
        }

        private static bool IsDenied(DecisionOutcome outcome)
        {
            if (!outcome.Applied || outcome.Envelope?.Result is not { } result) return false;
            if (result.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (result.TryGetProperty("allow", out var allow) &&
                allow.ValueKind == System.Text.Json.JsonValueKind.False)
                return true;
            if (result.TryGetProperty("eligible", out var eligible) &&
                eligible.ValueKind == System.Text.Json.JsonValueKind.False)
                return true;
            if (result.TryGetProperty("deny", out var deny) &&
                deny.ValueKind == System.Text.Json.JsonValueKind.True)
                return true;
            return false;
        }
    }
}
