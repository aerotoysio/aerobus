using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface ILayouts
    {
        Task<Layout?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Layout>> GetByEquipmentAsync(Guid equipmentId, CancellationToken ct = default);
        Task<IReadOnlyList<Layout>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<Layout>> ListByCompanyAsync(
            Guid companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Layout?> SaveAsync(Layout model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    // Layout is one aggregate document (compartments/seats/seat-types embedded).
    public sealed class Layouts(IDocumentStore store) : ILayouts
    {
        private readonly IDocumentStore _store = store;
        private const string C = DfCollections.Catalogue.Layouts;

        public Task<Layout?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _store.GetByIdAsync<Layout>(C, id, ct);

        public Task<IReadOnlyList<Layout>> GetByEquipmentAsync(Guid equipmentId, CancellationToken ct = default) =>
            _store.QueryAsync<Layout>(C, new Dictionary<string, object?> { ["EquipmentId"] = equipmentId }, ct: ct);

        public Task<IReadOnlyList<Layout>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _store.QueryAsync<Layout>(C, new Dictionary<string, object?> { ["CompanyId"] = companyId }, ct: ct);

        public Task<IReadOnlyList<Layout>> ListByCompanyAsync(
            Guid companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var f = new Dictionary<string, object?> { ["CompanyId"] = companyId };
            if (!string.IsNullOrWhiteSpace(status)) f["Status"] = status;
            return _store.QueryAsync<Layout>(C, f, pageNumber, pageSize, ct);
        }

        public async Task<Layout?> SaveAsync(Layout m, CancellationToken ct = default) =>
            await _store.UpsertAsync(C, m, m.Id, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _store.DeleteAsync(C, id, ct);
    }
}
