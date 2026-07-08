using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Model.Shopping;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>
    /// Builds the priced fare <see cref="ShopBundle"/>s for one flight solution.
    /// The RuleForge-backed implementation evaluates the ShopBundles decision
    /// point; on skip/degrade it returns an empty list plus a warning so the
    /// shop still returns solutions.
    /// </summary>
    public interface IShopBundleBuilder
    {
        Task<ShopBundleResult> BuildAsync(
            Guid companyId,
            IReadOnlyList<OfferShopPassenger> passengers,
            FlightSolution flightSolution,
            string origin,
            string destination,
            string currency,
            bool debug = false,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Result of a bundle build for one solution: the bundles produced (possibly
    /// empty), the RuleForge rule that produced them (when applied), and a
    /// warning when the decision degraded.
    /// </summary>
    public sealed record ShopBundleResult(
        List<ShopBundle> Bundles,
        string? RuleId,
        int? RuleVersion,
        string? Warning);
}
