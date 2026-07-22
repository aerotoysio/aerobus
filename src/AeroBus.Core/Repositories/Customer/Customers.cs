using AeroBus.Core.Data;

namespace AeroBus.Core.Repositories.Customer
{
    public interface ICustomers
    {
        Task<Model.Customer.Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Customer.Customer>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<Model.Customer.Customer?> GetByNumberAsync(string customerNumber, CancellationToken ct = default);

        /// <summary>Identity match by email (case-insensitive), company-scoped.</summary>
        Task<Model.Customer.Customer?> FindByEmailAsync(Guid companyId, string email, CancellationToken ct = default);

        /// <summary>Identity match by phone + surname (case-insensitive), company-scoped.</summary>
        Task<Model.Customer.Customer?> FindByPhoneAndLastNameAsync(
            Guid companyId, string phone, string lastName, CancellationToken ct = default);
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
        private const string C = DfCollections.Customer.Customers;

        public Task<Model.Customer.Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Model.Customer.Customer>(C, id, ct);

        public Task<IReadOnlyList<Model.Customer.Customer>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Model.Customer.Customer>(C, new Dictionary<string, object?> { [Df.Field(nameof(Model.Customer.Customer.CompanyId))] = companyId }, ct: ct);

        public Task<Model.Customer.Customer?> GetByNumberAsync(string customerNumber, CancellationToken ct = default) =>
            _store.GetByFieldAsync<Model.Customer.Customer>(C, Df.Field(nameof(Model.Customer.Customer.CustomerNumber)), customerNumber, ct);

        // Identity lookups use LIKE with NO wildcards: DocumentForge LIKE is
        // case-insensitive, so this is a case-insensitive equality — emails and
        // names match however they were originally cased.
        private static string CiEq(string field, string value) =>
            $"{field} LIKE '{value.Trim().Replace("'", "''")}'";

        public async Task<Model.Customer.Customer?> FindByEmailAsync(
            Guid companyId, string email, CancellationToken ct = default)
        {
            var where = $"{Df.CompanyId} = '{companyId}' AND " +
                        CiEq(Df.Field(nameof(Model.Customer.Customer.Email)), email);
            var rows = await _store.QueryWhereAsync<Model.Customer.Customer>(C, where, 1, 1, ct);
            return rows.Count > 0 ? rows[0] : null;
        }

        public async Task<Model.Customer.Customer?> FindByPhoneAndLastNameAsync(
            Guid companyId, string phone, string lastName, CancellationToken ct = default)
        {
            var where = $"{Df.CompanyId} = '{companyId}' AND " +
                        CiEq(Df.Field(nameof(Model.Customer.Customer.Phone)), phone) + " AND " +
                        CiEq(Df.Field(nameof(Model.Customer.Customer.LastName)), lastName);
            var rows = await _store.QueryWhereAsync<Model.Customer.Customer>(C, where, 1, 1, ct);
            return rows.Count > 0 ? rows[0] : null;
        }

        public Task<IReadOnlyList<Model.Customer.Customer>> ListByCompanyAsync(
            Guid companyId, string? loyaltyProgram, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { [Df.Field(nameof(Model.Customer.Customer.CompanyId))] = companyId };
            if (!string.IsNullOrWhiteSpace(loyaltyProgram)) f[Df.Field(nameof(Model.Customer.Customer.LoyaltyProgram))] = loyaltyProgram;
            if (!string.IsNullOrWhiteSpace(status)) f[Df.Field(nameof(Model.Customer.Customer.Status))] = status;
            return _store.QueryAsync<Model.Customer.Customer>(C, f, pageNumber, pageSize, ct);
        }

        public async Task<Model.Customer.Customer?> SaveAsync(Model.Customer.Customer m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
