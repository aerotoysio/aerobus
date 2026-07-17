using AeroBus.Core.Data;
using AeroBus.Core.Events;
using AeroBus.Core.Repositories.Customer;

namespace AeroBus.Core.Services.Customer
{
    public sealed class CustomersService(ICustomers repo, IEventPublisher events)
    {
        private readonly ICustomers _repo = repo;
        private readonly IEventPublisher _events = events;

        public Task<Model.Customer.Customer?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Model.Customer.Customer>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<Model.Customer.Customer?> GetByNumberAsync(
            string customerNumber,
            CancellationToken ct = default) =>
            _repo.GetByNumberAsync(customerNumber, ct);

        public Task<IReadOnlyList<Model.Customer.Customer>> ListByCompanyAsync(
            Guid companyId,
            string? loyaltyProgram,
            string? status,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(
                companyId,
                loyaltyProgram,
                status,
                search,
                pageNumber,
                pageSize,
                ct);

        public async Task<Model.Customer.Customer?> SaveAsync(
            Model.Customer.Customer model,
            CancellationToken ct = default)
        {
            // customer.created fires only for a genuinely new customer, so probe for
            // an existing document first (an update should not re-announce a create).
            var isNew = model.Id == Guid.Empty || await _repo.GetByIdAsync(model.Id, ct) is null;

            var saved = await _repo.SaveAsync(model, ct);
            if (isNew)
            {
                var c = saved ?? model;
                await _events.PublishAsync("customer.created",
                    new EventSubject(DfCollections.Customer.Customers, c.Id.ToString()),
                    new { id = c.Id, customerNumber = c.CustomerNumber, lastName = c.LastName, status = c.Status },
                    c.CompanyId, actor: "customers", ct);
            }
            return saved;
        }

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
