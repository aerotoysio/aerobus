using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class RightToFlyService(IRightsToFly repo)
    {
        private readonly IRightsToFly _repo = repo;

        public Task<RightToFly?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<RightToFly>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<RightToFly>> ListByCompanyAsync(
            Guid companyId, string? search, int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, search, pageNumber, pageSize, ct);

        public async Task<RightToFly?> SaveAsync(RightToFly model, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(model.RtfCode))
                throw new ArgumentException("RtfCode is required.");
            return await _repo.SaveAsync(
                model with { RtfCode = model.RtfCode.Trim().ToUpperInvariant() }, ct);
        }

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
