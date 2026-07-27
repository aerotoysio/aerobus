using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class LoyaltyTiersService(ILoyaltyTiers repo)
    {
        private readonly ILoyaltyTiers _repo = repo;

        public Task<LoyaltyTier?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<LoyaltyTier>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<LoyaltyTier>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, search, pageNumber, pageSize, ct);

        public async Task<LoyaltyTier?> SaveAsync(LoyaltyTier model, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(model.TierCode))
                throw new ArgumentException("TierCode is required.");
            return await _repo.SaveAsync(
                model with { TierCode = model.TierCode.Trim().ToUpperInvariant() }, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
