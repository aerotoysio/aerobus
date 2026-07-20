using AeroBus.Core.Data;
using AeroBus.Core.Model.Operations;

namespace AeroBus.Core.Repositories.Operations
{
    public interface ICheckIns
    {
        Task<PassengerCheckIn?> GetByIdAsync(Guid id, CancellationToken ct = default);
        /// <summary>The manifest: every passenger check-in row for a flight.</summary>
        Task<IReadOnlyList<PassengerCheckIn>> GetByFlightAsync(Guid flightId, CancellationToken ct = default);
        Task<PassengerCheckIn?> GetByFlightAndPassengerAsync(Guid flightId, Guid passengerId, CancellationToken ct = default);
        Task<IReadOnlyList<PassengerCheckIn>> GetByOrderAndFlightAsync(Guid orderId, Guid flightId, CancellationToken ct = default);
        Task<PassengerCheckIn?> SaveAsync(PassengerCheckIn model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    /// <summary>
    /// Per-(flight, passenger) departure-control documents (DfCollections.Operations.CheckIns collection).
    /// Written at booking time and advanced by the DCS surface; the FlightId key is
    /// top-level so the manifest is a single indexed query (mirrors
    /// <c>FlightInventories.GetByFlightAsync</c>).
    /// </summary>
    public sealed class CheckIns(IDocumentStore store) : DocumentRepository<PassengerCheckIn>(store), ICheckIns
    {
        protected override string Collection => DfCollections.Operations.CheckIns;

        public Task<IReadOnlyList<PassengerCheckIn>> GetByFlightAsync(Guid flightId, CancellationToken ct = default) =>
            QueryAsync(Eq(Df.Field(nameof(PassengerCheckIn.FlightId)), flightId), ct: ct);

        public async Task<PassengerCheckIn?> GetByFlightAndPassengerAsync(Guid flightId, Guid passengerId, CancellationToken ct = default)
        {
            var rows = await QueryAsync(new Dictionary<string, object?> { [Df.Field(nameof(PassengerCheckIn.FlightId))] = flightId, [Df.Field(nameof(PassengerCheckIn.PassengerId))] = passengerId }, ct: ct);
            return rows.Count > 0 ? rows[0] : null;
        }

        public Task<IReadOnlyList<PassengerCheckIn>> GetByOrderAndFlightAsync(Guid orderId, Guid flightId, CancellationToken ct = default) =>
            QueryAsync(new Dictionary<string, object?> { [Df.Field(nameof(PassengerCheckIn.OrderId))] = orderId, [Df.Field(nameof(PassengerCheckIn.FlightId))] = flightId }, ct: ct);
    }
}
