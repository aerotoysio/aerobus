using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IBundles
    {
        Task<Bundle?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Bundle>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Bundle>> GetPrettyByCompanyAsync(Guid companyId, CancellationToken ct = default);

        Task<IReadOnlyList<Bundle>> SearchAsync(
            Guid? companyId = null,
            string? search = null,
            string? status = null,
            string? type = null,
            string? category = null,
            int pageNumber = 1,
            int pageSize = 50,
            CancellationToken ct = default);

        Task<Bundle?> SaveAsync(Bundle model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class Bundles(IDocumentStore store) : DocumentRepository<Bundle>(store), IBundles
    {
        protected override string Collection => DfCollections.Catalogue.Bundles;

        public Task<IReadOnlyList<Bundle>> GetPrettyByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            QueryAsync(Eq(Df.Field(nameof(Bundle.CompanyId)), companyId), ct: ct);

        public Task<IReadOnlyList<Bundle>> SearchAsync(
            Guid? companyId = null,
            string? search = null,
            string? status = null,
            string? type = null,
            string? category = null,
            int pageNumber = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            var clauses = new List<string>();
            if (companyId is { } cid) clauses.Add($"{Df.Field(nameof(Bundle.CompanyId))} = '{cid}'");
            if (!string.IsNullOrWhiteSpace(status))
                clauses.Add($"{Df.Field(nameof(Bundle.Status))} = '{status.Replace("'", "''")}'");
            if (!string.IsNullOrWhiteSpace(type))
                clauses.Add($"{Df.Field(nameof(Bundle.Type))} = '{type.Replace("'", "''")}'");
            if (!string.IsNullOrWhiteSpace(category))
                clauses.Add($"{Df.Field(nameof(Bundle.Category))} = '{category.Replace("'", "''")}'");
            if (!string.IsNullOrWhiteSpace(search))
                clauses.Add(Df.Match(search, Df.Field(nameof(Bundle.Name)), Df.Field(nameof(Bundle.Type))));
            return QueryWhereAsync(string.Join(" AND ", clauses), pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
