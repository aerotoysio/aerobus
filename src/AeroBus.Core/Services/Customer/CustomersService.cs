using AeroBus.Core.Repositories.Customer;

namespace AeroBus.Core.Services.Customer
{
    public sealed class CustomersService(ICustomers repo)
    {
        private readonly ICustomers _repo = repo;

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

        public Task<Model.Customer.Customer?> SaveAsync(
            Model.Customer.Customer model,
            CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
