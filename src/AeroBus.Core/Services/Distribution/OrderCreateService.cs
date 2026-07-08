using AeroBus.Core.Events;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Model.Order;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Distribution;
using AeroBus.Core.Repositories.Order;
using AeroBus.Core.Rules;
using AeroBus.Core.Services.Stock;
using Microsoft.Extensions.Logging;
using OrderModel = AeroBus.Core.Model.Order.Order;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>
    /// Order-create runtime. Ported from the ooms OrderCreateService and extended
    /// for Phase 5 with the two behaviours that make an order real:
    /// <list type="number">
    ///   <item>a RuleForge <see cref="DecisionPoint.OrderValidate"/> policy hook
    ///   (best-effort; the local schema/state-machine checks are the hard gate); and</item>
    ///   <item>atomic seat-inventory decrement via <see cref="IInventoryService"/>
    ///   BEFORE the order is confirmed — with compensating releases if any leg
    ///   can't be sold, so a create either books every seat or none (never oversells,
    ///   never leaves the order confirmed with un-decremented inventory).</item>
    /// </list>
    ///
    /// The ooms request carried a fully-structured <c>Model.Offer.Offer</c>; AeroBus
    /// has no such type (OfferEngine is permanently dropped), so the request instead
    /// references the persisted <c>offers</c> document by id and the service binds
    /// the order against exactly what was shopped.
    /// </summary>
    public sealed class OrderCreateService
    {
        private readonly ICompanies _companies;
        private readonly IOrders _orders;
        private readonly IOffers _offers;
        private readonly IInventoryService _inventory;
        private readonly DecisionRunner _decisions;
        private readonly IEventPublisher _events;
        private readonly ILogger<OrderCreateService> _log;

        public OrderCreateService(
            ICompanies companies,
            IOrders orders,
            IOffers offers,
            IInventoryService inventory,
            DecisionRunner decisions,
            IEventPublisher events,
            ILogger<OrderCreateService> log)
        {
            _companies = companies;
            _orders = orders;
            _offers = offers;
            _inventory = inventory;
            _decisions = decisions;
            _events = events;
            _log = log;
        }

        public async Task<OrderCreateResult> Create(OrderCreateRequest request, Guid companyId, bool debug = false, CancellationToken ct = default)
        {
            // ── local validation (hard gate) ──────────────────────────────────
            var company = await _companies.GetByIdAsync(companyId, ct);
            if (company is null)
                return OrderCreateResult.Fail("companyNotFound", "Company not found.");

            if (request.Passengers is null || request.Passengers.Count == 0)
                return OrderCreateResult.Fail("noPassengers", "At least one passenger is required.");

            var offer = await _offers.GetByIdAsync(request.OfferId, ct);
            if (offer is null || offer.CompanyId != companyId)
                return OrderCreateResult.Fail("offerNotFound", "Offer not found.");
            if (offer.ExpiresAt is { } exp && exp < DateTime.UtcNow)
                return OrderCreateResult.Fail("offerExpired", "Offer has expired; re-shop.");

            // Choose the solution + bundle the caller booked (defaults: first
            // solution carrying bundles, cheapest priced bundle on it).
            var picked = PickSolution(offer, request.SolutionId, request.BundleId);
            if (picked is null)
                return OrderCreateResult.Fail("noBundle", "No priced bundle found on the offer for the requested selection.");
            var (solution, bundle) = picked.Value;

            // ── RuleForge OrderValidate (best-effort policy hook) ─────────────
            // Default failure mode is Allow: a RuleForge outage must never block a
            // legitimate order. Only an explicit Deny (or a Deny failure mode)
            // blocks — and even then the local checks above already gated.
            var validate = await _decisions.RunAsync(DecisionPoint.OrderValidate, new
            {
                companyId,
                channel = request.Channel,
                offerId = offer.Id,
                passengers = request.Passengers.Select(p => new { id = p.Id, type = p.PaxType, lastName = p.LastName }),
                bundleId = bundle.Id,
            }, debug, ct);

            if (IsDenied(validate))
            {
                _log.LogWarning("OrderValidate denied order for company {CompanyId}: {Warning}", companyId, validate.Warning);
                return OrderCreateResult.Fail("validationDenied", validate.Warning ?? "Order validation denied by policy.");
            }

            // ── build the order aggregate (still Pending) ─────────────────────
            var order = BuildOrder(request, company, offer, solution, bundle);

            // ── decrement inventory for every flight leg BEFORE confirming ────
            // One seat per passenger per (flight, bucket). Sell each leg in turn;
            // on the first failure, compensate by releasing every leg already sold
            // in THIS request, then return a 409-style failure (no confirmed order).
            var seats = request.Passengers.Count;
            var legs = DistinctLegs(order);
            var sold = new List<(Guid FlightId, string Bucket)>();

            foreach (var (flightId, bucket) in legs)
            {
                var sell = await _inventory.SellAsync(company.Id, flightId, bucket, seats, ct);
                if (sell.Success)
                    await _events.PublishAsync("inventory.adjusted",
                        new EventSubject("flightinventory", flightId.ToString()),
                        new { flightId, bucket, delta = -seats, reason = "order.create" },
                        company.Id, actor: "order-create", ct);
                if (!sell.Success)
                {
                    await CompensateAsync(company.Id, sold, seats, ct);
                    _log.LogWarning(
                        "Order create for company {CompanyId} failed to sell {Seats} seat(s) on flight {FlightId}/{Bucket}: {Reason}; released {Count} prior leg(s).",
                        company.Id, seats, flightId, bucket, sell.Reason, sold.Count);
                    return OrderCreateResult.Fail(sell.Reason, $"Could not book {seats} seat(s) on flight {flightId} ({bucket}): {sell.Reason}.");
                }
                sold.Add((flightId, bucket));
            }

            // All legs sold — confirm and persist. The order is one document, so
            // the whole aggregate (incl. passengers) is a single write.
            var confirmed = order with
            {
                Status = OrderStateMachine.Status.Confirmed,
                Updated = DateTime.UtcNow,
                History = new List<OrderHistory>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        FromStatus = OrderStateMachine.Status.Pending,
                        ToStatus = OrderStateMachine.Status.Confirmed,
                        Action = OrderStateMachine.Action.Confirm,
                        Reason = "Inventory secured on create.",
                        Created = DateTime.UtcNow,
                    },
                },
            };
            MarkItemsStatus(confirmed, "Confirmed");

            var saved = await _orders.SaveAsync(confirmed, ct) ?? confirmed;
            await _events.PublishAsync("order.created",
                new EventSubject("orders", saved.Id.ToString()),
                new
                {
                    orderId = saved.OrderId,
                    id = saved.Id,
                    status = saved.Status,
                    channel = saved.Channel,
                    currency = confirmed.OrderItems?.FirstOrDefault()?.Currency,
                    amount = confirmed.OrderItems?.FirstOrDefault()?.Amount,
                    passengers = saved.Passengers?.Count ?? 0,
                },
                saved.CompanyId, actor: "order-create", ct);

            return OrderCreateResult.Created(BuildView(saved));
        }

        // ── order assembly ────────────────────────────────────────────────────

        private static OrderModel BuildOrder(
            OrderCreateRequest request,
            Model.Admin.Company company,
            Offer offer,
            Model.Shopping.FlightSolution solution,
            ShopBundle bundle)
        {
            var now = DateTime.UtcNow;
            var companyId = company.Id;

            foreach (var pax in request.Passengers)
            {
                pax.Id = pax.Id == Guid.Empty ? Guid.NewGuid() : pax.Id;
                pax.Created ??= now;
                pax.Updated = now;
                if (string.IsNullOrWhiteSpace(pax.Status)) pax.Status = "Active";
                pax.CompanyId ??= companyId;
            }

            var currency = bundle.Price?.Currency ?? offer.Currency ?? "AED";
            var bundleAmount = bundle.Price?.Total ?? 0m;

            // Flight services on the fare item come from the solution's legs. Bucket
            // = the solution cabin (layout compartment) or "ALL" (single-bucket flight).
            var bucket = string.IsNullOrWhiteSpace(solution.Cabin) ? "ALL" : solution.Cabin!;
            var flightIds = ParseFlightIds(solution);

            var services = new List<Service>();
            foreach (var pax in request.Passengers)
            {
                var flightServices = flightIds
                    .Select(fid => new FlightService
                    {
                        Id = Guid.NewGuid(),
                        FlightId = fid,
                        Bucket = bucket,
                        Status = "Open",
                    })
                    .ToList();

                services.Add(new Service
                {
                    Id = Guid.NewGuid(),
                    Created = now,
                    Updated = now,
                    Type = "Flight",
                    Status = "Open",
                    PassengerId = pax.Id,
                    FlightServices = flightServices,
                });
            }

            var charges = new List<OrderItemCharge>();
            if (bundle.Price is { } price)
            {
                charges.Add(new OrderItemCharge
                {
                    Id = Guid.NewGuid(),
                    AmountType = "Base",
                    Code = "BASE",
                    Currency = currency,
                    Amount = price.Base,
                    Status = "Active",
                    Created = now,
                    Updated = now,
                });
                if (price.Taxes > 0m)
                    charges.Add(new OrderItemCharge
                    {
                        Id = Guid.NewGuid(),
                        AmountType = "Tax",
                        Code = "TAX",
                        Currency = currency,
                        Amount = price.Taxes,
                        Status = "Active",
                        Created = now,
                        Updated = now,
                    });
            }

            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid(),
                Created = now,
                Updated = now,
                Type = "Flight",
                Name = bundle.Name ?? bundle.BundleCode ?? "Fare",
                Description = bundle.Description,
                Status = "Open",
                Amount = bundleAmount,
                Currency = currency,
                Charges = charges,
                Services = services,
            };

            var payments = new List<Payment>();
            if (request.Payment is { } pay)
            {
                payments.Add(new Payment
                {
                    Id = Guid.NewGuid(),
                    Provider = pay.Provider,
                    Method = pay.Method,
                    Currency = string.IsNullOrWhiteSpace(pay.Currency) ? currency : pay.Currency,
                    AuthorizedAmount = bundleAmount,
                    CapturedAmount = 0m,
                    RefundedAmount = 0m,
                    Status = "Pending",
                    Created = now,
                    Updated = now,
                });
            }

            // ooms fed OrderSequence from a SQL IDENTITY that no longer exists;
            // DocumentForge has no sequence generator, so derive a non-negative
            // 31-bit sequence per order to keep OrderIdentification's bijective
            // scheme producing distinct codes. (A durable per-company counter is a
            // later concern — order id uniqueness is not load-bearing for Phase 5.)
            var sequence = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);

            var order = new OrderModel
            {
                Id = Guid.NewGuid(),
                Channel = request.Channel,
                Type = "Flight",
                CompanyId = companyId,
                Created = now,
                Updated = now,
                ConcurrencyId = Guid.NewGuid(),
                OrderSequence = sequence,
                Status = OrderStateMachine.Status.Pending,
                OrderItems = new List<OrderItem> { orderItem },
                Payments = payments,
                Passengers = request.Passengers,
                History = new List<OrderHistory>(),
            };

            order.OrderId = OrderIdentification.Generate(
                company.Designator, company.AccountingCode, order.OrderSequence);

            return order;
        }

        // ── selection + mapping helpers ────────────────────────────────────────

        private static (Model.Shopping.FlightSolution Solution, ShopBundle Bundle)? PickSolution(
            Offer offer, Guid? solutionId, Guid? bundleId)
        {
            var solutions = offer.OriginDestinations
                .SelectMany(od => od.FlightSolutions)
                .ToList();

            // Explicit solution id wins; otherwise search every solution.
            var candidates = solutionId is { } sid
                ? solutions.Where(s => s.Id == sid)
                : solutions;

            foreach (var solution in candidates)
            {
                var bundles = solution.Bundles ?? new List<ShopBundle>();
                var bundle = bundleId is { } bid
                    ? bundles.FirstOrDefault(b => b.Id == bid)
                    : bundles.Where(b => b.Price is not null).OrderBy(b => b.Price!.Total).FirstOrDefault();
                if (bundle is not null)
                    return (solution, bundle);
            }

            return null;
        }

        private static List<Guid> ParseFlightIds(Model.Shopping.FlightSolution solution) =>
            (solution.Flights ?? new List<Model.Shopping.FlightSegment>())
                .Select(f => Guid.TryParse(f.FlightRef, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .Distinct()
                .ToList();

        /// <summary>Distinct (FlightId, Bucket) legs across the order's flight services.</summary>
        private static List<(Guid FlightId, string Bucket)> DistinctLegs(OrderModel order) =>
            (order.OrderItems ?? new List<OrderItem>())
                .SelectMany(i => i.Services ?? new List<Service>())
                .SelectMany(s => s.FlightServices ?? new List<FlightService>())
                .Where(fs => fs.FlightId is { } fid && fid != Guid.Empty)
                .Select(fs => (fs.FlightId!.Value, string.IsNullOrWhiteSpace(fs.Bucket) ? "ALL" : fs.Bucket!))
                .Distinct()
                .ToList();

        private async Task CompensateAsync(
            Guid companyId, IReadOnlyList<(Guid FlightId, string Bucket)> sold, int seats, CancellationToken ct)
        {
            foreach (var (flightId, bucket) in sold)
            {
                try
                {
                    await _inventory.ReleaseAsync(companyId, flightId, bucket, seats, ct);
                    await _events.PublishAsync("inventory.adjusted",
                        new EventSubject("flightinventory", flightId.ToString()),
                        new { flightId, bucket, delta = seats, reason = "order.create.compensate" },
                        companyId, actor: "order-create", ct);
                }
                catch (Exception ex)
                {
                    // A compensation failure must not mask the original create
                    // failure; log loudly and continue releasing the rest.
                    _log.LogError(ex,
                        "Compensating release failed for flight {FlightId}/{Bucket} ({Seats} seats) during order-create rollback.",
                        flightId, bucket, seats);
                }
            }
        }

        private static void MarkItemsStatus(OrderModel order, string status)
        {
            foreach (var item in order.OrderItems ?? new List<OrderItem>())
            {
                item.Status = status;
                foreach (var svc in item.Services ?? new List<Service>())
                    svc.Status = status;
            }
        }

        private static bool IsDenied(DecisionOutcome outcome)
        {
            // A rule that applied and returned an explicit allow=false denies;
            // otherwise (applied-allow, skipped, or degraded under Allow mode) proceed.
            if (!outcome.Applied || outcome.Envelope?.Result is not { } result) return false;
            if (result.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (result.TryGetProperty("allow", out var allow) &&
                allow.ValueKind == System.Text.Json.JsonValueKind.False)
                return true;
            if (result.TryGetProperty("deny", out var deny) &&
                deny.ValueKind == System.Text.Json.JsonValueKind.True)
                return true;
            return false;
        }

        private static OrderView BuildView(OrderModel order)
        {
            var charges = (order.OrderItems ?? new List<OrderItem>())
                .SelectMany(i => i.Charges ?? new List<OrderItemCharge>())
                .ToList();
            return new OrderView
            {
                Order = order,
                Passengers = order.Passengers ?? new(),
                Payments = order.Payments ?? new(),
                Charges = charges,
            };
        }
    }

    /// <summary>
    /// Result of an order-create. Carries the created order view on success, or a
    /// stable reason code + message on failure (mapped to a 409 at the endpoint for
    /// inventory/validation failures, 404 for not-found). No ooms Response base
    /// wrapper — the fields live here directly (as Phase 4's offer models do).
    /// </summary>
    public sealed class OrderCreateResult
    {
        public bool Success { get; init; }
        public string? Reason { get; init; }
        public string? Message { get; init; }
        public OrderView? Order { get; init; }

        public static OrderCreateResult Created(OrderView view) => new() { Success = true, Order = view };
        public static OrderCreateResult Fail(string reason, string message) =>
            new() { Success = false, Reason = reason, Message = message };
    }
}
