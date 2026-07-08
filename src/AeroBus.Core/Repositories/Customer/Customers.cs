using AeroBus.Core.Data;

namespace AeroBus.Core.Repositories.Customer
{
    public interface ICustomers
    {
        Task<Model.Customer.Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Customer.Customer>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<Model.Customer.Customer?> GetByNumberAsync(string customerNumber, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Customer.Customer>> ListByCompanyAsync(
            Guid companyId, string? loyaltyProgram, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Model.Customer.Customer?> SaveAsync(Model.Customer.Customer model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    // Customer is one aggregate document (Passports + StoredCards embedded); the
    // model is not an IDocument marker type (kept faithful to ooms), so this
    // repository talks to IDocumentStore directly.
    public sealed class Customers(IDocumentStore store) : ICustomers
    {
        private readonly IDocumentStore _store = store;
        private const string C = "customers";

        public Task<Model.Customer.Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Model.Customer.Customer>(C, id, ct);

        public Task<IReadOnlyList<Model.Customer.Customer>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Model.Customer.Customer>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, ct: ct);

        public Task<Model.Customer.Customer?> GetByNumberAsync(string customerNumber, CancellationToken ct = default) =>
            _store.GetByFieldAsync<Model.Customer.Customer>(C, "CustomerNumber", customerNumber, ct);

        public Task<IReadOnlyList<Model.Customer.Customer>> ListByCompanyAsync(
            Guid companyId, string? loyaltyProgram, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["CompanyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(loyaltyProgram)) f["LoyaltyProgram"] = loyaltyProgram;
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            return _store.QueryAsync<Model.Customer.Customer>(C, f, pageNumber, pageSize, ct);
        }

        public async Task<Model.Customer.Customer?> SaveAsync(Model.Customer.Customer m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
