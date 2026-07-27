using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface ILoyaltyTiers
    {
        Task<LoyaltyTier?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<LoyaltyTier>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<LoyaltyTier>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<LoyaltyTier?> SaveAsync(LoyaltyTier model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    public sealed class LoyaltyTiers(IDocumentStore store) : DocumentRepository<LoyaltyTier>(store), ILoyaltyTiers
    {
        protected override string Collection => DfCollections.Catalogue.LoyaltyTiers;

        public Task<IReadOnlyList<LoyaltyTier>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var where = $"{Df.Field(nameof(LoyaltyTier.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND " + Df.Match(search,
                    Df.Field(nameof(LoyaltyTier.TierCode)),
                    Df.Field(nameof(LoyaltyTier.TierName)),
                    Df.Field(nameof(LoyaltyTier.Description)));
            where += $" ORDER BY {Df.Field(nameof(LoyaltyTier.Rank))}";
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
