using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class FlightSolutionsService(IFlightSolutions s)
    {
        private readonly IFlightSolutions _s = s;

        public Task<FlightSolution?> SearchAsync(FlightSolutionQuery q, CancellationToken ct = default)
            => _s.SearchAsync(q, ct);

        public Task<FlightSolution?> SearchCsaAsync(FlightSolutionQuery q, CancellationToken ct = default)
            => _s.SearchCsaAsync(q, ct);

        public Task<IReadOnlyList<FlightSolution>> SearchKAsync(FlightSolutionQuery q, CancellationToken ct = default)
            => _s.SearchKAsync(q, ct);
    }
}
