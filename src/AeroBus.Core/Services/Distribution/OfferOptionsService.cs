using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Repositories.Distribution;

namespace AeroBus.Core.Services.Distribution
{
    public sealed record OfferOptionsResult(
        Guid OfferId,
        Guid FlightSolutionId,
        string RtfCode,
        string? Currency,
        IReadOnlyList<BundleServiceItem> Products);

    /// <summary>
    /// The à-la-carte call (Shape 3, docs/rule-based-retailing.md): after the
    /// customer picks a Right to Fly on a shopped offer, the same connected
    /// policies run again with <c>mode: "optional"</c> and emit the PRICED
    /// extras (excess bags, paid seats, lounge) instead of the freebies.
    /// </summary>
    public sealed class OfferOptionsService(
        IOffers offers,
        IRightsToFly rightsToFly,
        RuleForgeShopBundleBuilder builder)
    {
        public async Task<OfferOptionsResult> GetOptionsAsync(
            Guid companyId, Guid offerId, Guid flightSolutionId, string rtfCode, CancellationToken ct = default)
        {
            var offer = await offers.GetByIdAsync(offerId, ct)
                        ?? throw new KeyNotFoundException($"Offer {offerId} was not found.");
            if (offer.CompanyId is { } oc && oc != companyId)
                throw new KeyNotFoundException($"Offer {offerId} was not found.");

            var match = offer.OriginDestinations
                .SelectMany(od => od.FlightSolutions.Select(fs => (od, fs)))
                .FirstOrDefault(x => x.fs.Id == flightSolutionId);
            if (match.fs is null)
                throw new KeyNotFoundException($"Flight solution {flightSolutionId} is not part of offer {offerId}.");

            var rtf = (await rightsToFly.GetByCompanyAsync(companyId, ct))
                .FirstOrDefault(r => string.Equals(r.RtfCode, rtfCode, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Right to Fly '{rtfCode}' was not found.");

            var products = await builder.RunPoliciesAsync(
                rtf, "optional", offer.Passengers, match.fs,
                match.od.Origin ?? "", match.od.Destination ?? "", offer.Currency ?? "USD", ct);

            return new OfferOptionsResult(offerId, flightSolutionId, rtf.RtfCode!, offer.Currency, products);
        }
    }
}
