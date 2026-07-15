using AeroBus.Core.Model.Operations;
using AeroBus.Core.Repositories.Operations;
using Microsoft.Extensions.Logging;
using OrderModel = AeroBus.Core.Model.Order.Order;

namespace AeroBus.Core.Services.Operations
{
    public interface IManifestBuilder
    {
        /// <summary>
        /// Materialise the departure-control manifest for a freshly-confirmed order:
        /// one <see cref="PassengerCheckIn"/> row per (passenger, flight) at status
        /// <see cref="CheckInStatus.Booked"/>. This is why the manifest is queryable
        /// by flight — the FlightId is buried three levels deep in the order document,
        /// so we index it out to a flat, flight-keyed collection at booking time.
        /// Best-effort: a failure here must never fail the booking.
        /// </summary>
        Task BuildForOrderAsync(OrderModel order, CancellationToken ct = default);
    }

    public sealed class ManifestBuilder(ICheckIns checkIns, ILogger<ManifestBuilder> log) : IManifestBuilder
    {
        private readonly ICheckIns _checkIns = checkIns;
        private readonly ILogger<ManifestBuilder> _log = log;

        public async Task BuildForOrderAsync(OrderModel order, CancellationToken ct = default)
        {
            try
            {
                var companyId = order.CompanyId ?? Guid.Empty;
                var byId = (order.Passengers ?? new()).ToDictionary(p => p.Id);
                var now = DateTime.UtcNow;

                foreach (var item in order.OrderItems ?? new())
                foreach (var svc in item.Services ?? new())
                {
                    if (svc.PassengerId is not { } passengerId) continue;
                    foreach (var fs in svc.FlightServices ?? new())
                    {
                        if (fs.FlightId is not { } flightId || flightId == Guid.Empty) continue;

                        // Idempotent: one row per (flight, passenger). Re-runs skip.
                        if (await _checkIns.GetByFlightAndPassengerAsync(flightId, passengerId, ct) is not null)
                            continue;

                        byId.TryGetValue(passengerId, out var pax);
                        await _checkIns.SaveAsync(new PassengerCheckIn
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            FlightId = flightId,
                            OrderId = order.Id,
                            PassengerId = passengerId,
                            FirstName = pax?.FirstName ?? string.Empty,
                            LastName = pax?.LastName ?? string.Empty,
                            PaxType = pax?.PaxType ?? string.Empty,
                            BookedBucket = fs.Bucket,
                            Status = CheckInStatus.Booked,
                            SeatRow = fs.SeatRow,
                            SeatColumn = fs.SeatColumn,
                            Created = now,
                            Updated = now,
                        }, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                // The commercial order is already persisted; a manifest hiccup must
                // not fail the booking (mirrors the best-effort event publisher).
                _log.LogError(ex, "Failed to build the DCS manifest for order {OrderId}; it can be rebuilt.", order.Id);
            }
        }
    }
}
