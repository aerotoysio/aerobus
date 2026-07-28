using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IRightsToFly
    {
        Task<RightToFly?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<RightToFly>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<RightToFly>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<RightToFly?> SaveAsync(RightToFly model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class RightsToFly(IDocumentStore store) : DocumentRepository<RightToFly>(store), IRightsToFly
    {
        protected override string Collection => DfCollections.Catalogue.RightToFly;

        public Task<IReadOnlyList<RightToFly>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var where = $"{Df.Field(nameof(RightToFly.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND " + Df.Match(search,
                    Df.Field(nameof(RightToFly.RtfCode)),
                    Df.Field(nameof(RightToFly.RtfName)),
                    Df.Field(nameof(RightToFly.Description)));
            where += $" ORDER BY {Df.Field(nameof(RightToFly.Rank))}";
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
