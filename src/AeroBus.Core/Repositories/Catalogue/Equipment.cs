using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Repositories.Catalogue
{
    public interface IEquipment
    {
        Task<Equipment?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default);

        Task<IReadOnlyList<Equipment>> GetByCompanyAsync(
            Guid companyId,
            CancellationToken ct = default);

        // Main list operation – paged + search for a company
        Task<IReadOnlyList<Equipment>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default);

        Task<Equipment?> SaveAsync(
            Equipment model,
            CancellationToken ct = default);

        Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default);
    }

    public sealed class EquipmentRepo(IDocumentStore store) : DocumentRepository<Equipment>(store), IEquipment
    {
        protected override string Collection => DfCollections.Catalogue.Equipment;

        public Task<IReadOnlyList<Equipment>> ListByCompanyAsync(
            Guid companyId,
            string? status,
            string? search,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default)
        {
            var where = $"{Df.Field(nameof(Model.Catalogue.Equipment.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(status))
                where += $" AND {Df.Field(nameof(Model.Catalogue.Equipment.Status))} = '{status.Replace("'", "''")}'";
            if (!string.IsNullOrWhiteSpace(search))
            {
                // DF LIKE is case-insensitive; match type code or name.
                var q = Df.Contains(search);
                where += $" AND ({Df.Field(nameof(Model.Catalogue.Equipment.EquipmentCode))} LIKE '{q}'" +
                         $" OR {Df.Field(nameof(Model.Catalogue.Equipment.Name))} LIKE '{q}')";
            }
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        public Task<bool> DeleteAsync(
            Guid id,
            Guid concurrencyId,
            CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
