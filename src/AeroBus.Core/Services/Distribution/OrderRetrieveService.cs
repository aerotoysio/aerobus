using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Model.Order;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Order;
using OrderModel = AeroBus.Core.Model.Order.Order;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>
    /// Order retrieval / view projection. Ported from the ooms OrderRetrieveService
    /// (file <c>OrderRetrieve.cs</c>). The order is a single document — items,
    /// services, charges, payments and passengers all arrive embedded — so retrieve
    /// just reads and projects. Lookup is by the customer-facing OrderId (with an
    /// optional last-name check) or by the internal Guid.
    /// </summary>
    public sealed class OrderRetrieveService
    {
        private readonly ICompanies _companies;
        private readonly IOrders _orders;

        public OrderRetrieveService(ICompanies companies, IOrders orders)
        {
            _companies = companies;
            _orders = orders;
        }

        public async Task<OrderViewResponse> Retrieve(OrderRetrieveRequest request, Guid companyId, CancellationToken ct = default)
        {
            var response = new OrderViewResponse();

            var company = await _companies.GetByIdAsync(companyId, ct);
            if (company is null)
            {
                response.Success = false;
                response.ErrorMessage = "Company not found.";
                return response;
            }

            OrderModel? order = null;
            if (!string.IsNullOrWhiteSpace(request.OrderId))
                order = await _orders.GetByOrderIdAsync(request.OrderId!, ct);
            else if (request.Id != Guid.Empty)
                order = await _orders.GetByIdAsync(request.Id, ct);

            if (order is null || order.CompanyId != companyId)
            {
                response.Success = false;
                response.ErrorMessage = "Order not found.";
                return response;
            }

            var orderView = RetrieveOrder(order);

            // When retrieving by the public OrderId, gate on the passenger last name
            // (a lightweight self-service check, mirroring ooms). A by-Guid lookup
            // (internal/admin) skips the name gate.
            if (!string.IsNullOrWhiteSpace(request.OrderId) && !string.IsNullOrWhiteSpace(request.LastName))
            {
                var wanted = request.LastName!.Trim();
                var match = orderView.Passengers.Any(p =>
                    string.Equals(p.LastName?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
                if (match)
                    response.Orders.Add(orderView);
                else
                {
                    response.Success = false;
                    response.ErrorMessage = "Order not found.";
                }
            }
            else
            {
                response.Orders.Add(orderView);
            }

            return response;
        }

        // The whole order is one document: items/services/charges/payments/passengers
        // all arrive embedded — just project them into the view.
        public OrderView RetrieveOrder(OrderModel order)
        {
            var passengers = order.Passengers ?? new List<Model.Customer.Passenger>();
            var payments = order.Payments ?? new List<Payment>();

            var allCharges = new List<OrderItemCharge>();
            foreach (var orderItem in order.OrderItems ?? new List<OrderItem>())
                if (orderItem.Charges is { } charges)
                    allCharges.AddRange(charges);

            return new OrderView
            {
                Order = order,
                Passengers = passengers,
                Payments = payments,
                Charges = allCharges,
            };
        }
    }
}
