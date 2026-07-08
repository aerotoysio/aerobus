using AeroBus.Core.Data;
using AeroBus.Core.Model.Order;

namespace AeroBus.Core.Repositories.Order
{
    public interface IOrders
    {
        Task<Model.Order.Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Model.Order.Order?> GetByOrderIdAsync(string orderId, CancellationToken ct = default);
        Task<IReadOnlyList<Model.Order.Order>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
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
        protected override string Collection => "orders";

        public Task<Model.Order.Order?> GetByOrderIdAsync(string orderId, CancellationToken ct = default) =>
            GetByFieldAsync("OrderId", orderId, ct);

        // events: order.created / order.changed via outbox in Phase 6
    }
}
