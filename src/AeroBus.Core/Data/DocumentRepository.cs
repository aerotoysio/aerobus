using AeroBus.Core.Model;

namespace AeroBus.Core.Data
{
    /// <summary>
    /// Generic CRUD base for Guid-keyed aggregates. Derived repositories set
    /// <see cref="Collection"/> and add their bespoke queries via the protected
    /// helpers; the common interface members (get-by-id, get-by-company, save,
    /// delete) come from here.
    /// </summary>
    public abstract class DocumentRepository<T>(IDocumentStore store) where T : class, IDocument
    {
        protected IDocumentStore Store { get; } = store;

        protected abstract string Collection { get; }

        public virtual Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Store.GetByIdAsync<T>(Collection, id, ct);

        public virtual Task<IReadOnlyList<T>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Store.QueryAsync<T>(Collection, Eq("CompanyId", companyId), ct: ct);

        public virtual Task<T?> SaveAsync(T model, CancellationToken ct = default) =>
            Store.UpsertAsync(Collection, model, model.Id, ct);

        public virtual Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
            Store.DeleteAsync(Collection, id, ct);

        protected Task<T?> GetByFieldAsync(string field, string value, CancellationToken ct = default) =>
            Store.GetByFieldAsync<T>(Collection, field, value, ct);

        protected Task<IReadOnlyList<T>> ListAsync(int? page = null, int? size = null, CancellationToken ct = default) =>
            Store.QueryAsync<T>(Collection, new Dictionary<string, object?>(), page, size, ct);

        protected Task<IReadOnlyList<T>> QueryAsync(
            Dictionary<string, object?> filters,
            int? page = null,
            int? size = null,
            CancellationToken ct = default) =>
            Store.QueryAsync<T>(Collection, filters, page, size, ct);

        protected Task<IReadOnlyList<T>> QueryWhereAsync(
            string where,
            int? page = null,
            int? size = null,
            CancellationToken ct = default) =>
            Store.QueryWhereAsync<T>(Collection, where, page, size, ct);

        protected Task<int> CountAsync(Dictionary<string, object?> filters, CancellationToken ct = default) =>
            Store.CountAsync(Collection, filters, ct);

        protected static Dictionary<string, object?> Eq(string field, object? value) =>
            new() { [field] = value };
    }
}
