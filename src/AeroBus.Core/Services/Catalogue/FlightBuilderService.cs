using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class FlightBuilderService(IFlightBuilder b)
    {
        private readonly IFlightBuilder _b = b;

        public Task<IReadOnlyList<Model.Catalogue.Flight>> PreviewAsync(Guid scheduleId, CancellationToken ct = default)
            => _b.PreviewAsync(scheduleId, ct);

        public Task<IReadOnlyList<Model.Catalogue.Flight>> BuildAsync(Guid scheduleId, CancellationToken ct = default)
            => _b.BuildAsync(scheduleId, ct);
    }
}
