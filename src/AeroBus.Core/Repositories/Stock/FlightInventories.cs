using AeroBus.Core.Data;
using AeroBus.Core.Model.Stock;

namespace AeroBus.Core.Repositories.Stock
{
    public interface IFlightInventories
    {
        Task<FlightInventory?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<FlightInventory>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<IReadOnlyList<FlightInventory>> GetByFlightAsync(Guid flightId, CancellationToken ct = default);
        Task<FlightInventory?> SaveAsync(FlightInventory model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default);
    }

    /// <summary>
    /// Per-flight, per-bucket inventory documents ("flightinventory" collection).
    /// Created by the flight builder; sell/refund flows will mutate Sold/Available
    /// via the DocumentForge conditional-update primitive (top-level counters).
    /// </summary>
    public sealed class FlightInventories(IDocumentStore store) : DocumentRepository<FlightInventory>(store), IFlightInventories
    {
        protected override string Collection => "flightinventory";

        public Task<IReadOnlyList<FlightInventory>> GetByFlightAsync(Guid flightId, CancellationToken ct = default) =>
            QueryAsync(Eq("FlightId", flightId), ct: ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            base.DeleteAsync(id, ct);
    }
}
