using AeroBus.Core.Data;
using AeroBus.Core.Model.Order;

namespace AeroBus.Core.Repositories.Order
{
    public interface IOrders
    {
        Task<Model.Order.Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Model.Order.Order?> GetByOrderIdAsync(string orderId, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Order.Order>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);

        /// <summary>Paged order list for the caller's company, newest first; search matches the public order id.</summary>
        Task<IReadOnlyList<Model.Order.Order>> ListByCompanyAsync(
            Guid companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default);
        Task<Model.Order.Order?> SaveAsync(Model.Order.Order model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    /// <summary>
    /// The order aggregate is a single DocumentForge document — one repository,
    /// children (items/services/charges/payments/passengers/history) embedded.
    /// Ported from ooms Business.Order.Orders onto the AeroBus
    /// <see cref="DocumentRepository{T}"/> base.
    /// </summary>
    public sealed class Orders(IDocumentStore store)
        : DocumentRepository<Model.Order.Order>(store), IOrders
    {
        protected override string Collection => DfCollections.Order.Orders;

        public Task<Model.Order.Order?> GetByOrderIdAsync(string orderId, CancellationToken ct = default) =>
            GetByFieldAsync(Df.Field(nameof(Model.Order.Order.OrderId)), orderId, ct);

        public Task<IReadOnlyList<Model.Order.Order>> ListByCompanyAsync(
            Guid companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default)
        {
            var where = $"{Df.Field(nameof(Model.Order.Order.CompanyId))} = '{companyId}'";
            if (!string.IsNullOrWhiteSpace(status))
                where += $" AND {Df.Field(nameof(Model.Order.Order.Status))} = '{status.Replace("'", "''")}'";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND " + Df.Match(search, Df.Field(nameof(Model.Order.Order.OrderId)));
            where += $" ORDER BY {Df.Created} DESC";
            return QueryWhereAsync(where, pageNumber, pageSize, ct);
        }

        // events: order.created / order.changed via outbox in Phase 6
    }
}
