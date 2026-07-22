using AeroBus.Core.Data;
using AeroBus.Core.Events;
using AeroBus.Core.Repositories.Customer;
using Microsoft.Extensions.Logging;
using PassengerModel = AeroBus.Core.Model.Customer.Passenger;

namespace AeroBus.Core.Services.Customer
{
    /// <summary>
    /// The single-identity pattern: passengers on an order are INSTANCES (a
    /// snapshot of who travelled), the customers collection is the durable
    /// identity. At order create, each passenger with contact data is matched to
    /// a customer — by email first, then phone + surname — created when new, and
    /// stamped with <c>CustomerId</c>. The first linked passenger becomes the
    /// order's profile. Best-effort by contract: identity work must never fail a
    /// booking, so callers treat a null result as "no link".
    /// </summary>
    public sealed class CustomerLinker
    {
        private readonly ICustomers _customers;
        private readonly IEventPublisher _events;
        private readonly ILogger<CustomerLinker> _log;

        public CustomerLinker(ICustomers customers, IEventPublisher events, ILogger<CustomerLinker> log)
        {
            _customers = customers;
            _events = events;
            _log = log;
        }

        /// <summary>
        /// Link every passenger that carries contact data; returns the lead
        /// (first linked) customer id for the order's profile, or null when no
        /// passenger could be identified.
        /// </summary>
        public async Task<Guid?> LinkAsync(
            Guid companyId, IReadOnlyList<PassengerModel> passengers, CancellationToken ct = default)
        {
            Guid? lead = null;
            foreach (var pax in passengers)
            {
                try
                {
                    var customerId = await LinkOneAsync(companyId, pax, ct);
                    if (customerId is { } id)
                    {
                        pax.CustomerId = id;
                        lead ??= id;
                    }
                }
                catch (Exception ex)
                {
                    // Identity is a convenience, the booking is the product.
                    _log.LogWarning(ex,
                        "Customer link for passenger {Last}/{First} failed; the order proceeds unlinked.",
                        pax.LastName, pax.FirstName);
                }
            }
            return lead;
        }

        private async Task<Guid?> LinkOneAsync(Guid companyId, PassengerModel pax, CancellationToken ct)
        {
            var email = Normalize(pax.Email);
            var phone = NormalizePhone(pax.Phone);
            if (email is null && (phone is null || string.IsNullOrWhiteSpace(pax.LastName)))
                return null; // nothing to identify by (typical for children/infants)

            var existing = email is not null
                ? await _customers.FindByEmailAsync(companyId, email, ct)
                : null;
            existing ??= phone is not null && !string.IsNullOrWhiteSpace(pax.LastName)
                ? await _customers.FindByPhoneAndLastNameAsync(companyId, phone, pax.LastName, ct)
                : null;

            if (existing is not null)
                return existing.Id;

            var customer = new Model.Customer.Customer
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                CustomerNumber = $"CU{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                Title = pax.Title,
                FirstName = pax.FirstName,
                LastName = pax.LastName,
                Email = email,
                Phone = phone,
                Status = "Active",
                Created = DateTime.UtcNow,
            };
            await _customers.SaveAsync(customer, ct);

            await _events.PublishAsync("customer.created",
                new EventSubject(DfCollections.Customer.Customers, customer.Id.ToString()),
                new { customer.Id, customer.CustomerNumber, customer.LastName },
                companyId, actor: "customer-linker", ct);

            return customer.Id;
        }

        private static string? Normalize(string? email)
        {
            var e = email?.Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(e) || !e.Contains('@') ? null : e;
        }

        private static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return digits.Length >= 6 ? digits : null;
        }
    }
}
