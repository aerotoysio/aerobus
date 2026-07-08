using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class LayoutsService(ILayouts repo)
    {
        private readonly ILayouts _repo = repo;

        public Task<Layout?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Layout>> GetByEquipmentAsync(
            Guid equipmentId,
            CancellationToken ct = default) =>
            _repo.GetByEquipmentAsync(equipmentId, ct);

        public Task<IReadOnlyList<Layout>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<Layout>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default) =>
            _repo.ListByCompanyAsync(companyId, status, search, pageNumber, pageSize, ct);

        public Task<Layout?> SaveAsync(
            Layout model,
            CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);
    }
}
