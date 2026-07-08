using System.Text.Json;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Repositories.Distribution;
using AeroBus.Core.Rules;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>Request to re-price a previously shopped offer.</summary>
    public sealed class OfferPriceRequest
    {
        public Guid OfferId { get; set; }
        public string? Currency { get; set; }
    }

    /// <summary>Re-priced breakdown of a shopped offer.</summary>
    public sealed class OfferPriceResponse
    {
        public Guid OfferId { get; set; }
        public string? Currency { get; set; }
        public PricingSummary PricingSummary { get; set; } = new();
        public List<OriginDestinationResponse> OriginDestinations { get; set; } = new();
        public string? RuleId { get; set; }
        public int? RuleVersion { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Re-prices a shopped offer via the RuleForge OfferPricing decision point.
    /// The ooms /offer/price was a stub (empty response) — this is a thin real
    /// implementation that keeps the route: look the offer up, run the pricing
    /// decision, and return the (re-priced or as-shopped) breakdown. When
    /// RuleForge is unavailable it degrades to the offer's shopped prices with a
    /// warning rather than failing.
    /// </summary>
    public sealed class OfferPriceService
    {
        private readonly IOffers _offers;
        private readonly DecisionRunner _decisions;
        private readonly ILogger<OfferPriceService> _log;

        public OfferPriceService(IOffers offers, DecisionRunner decisions, ILogger<OfferPriceService> log)
        {
            _offers = offers;
            _decisions = decisions;
            _log = log;
        }

        public async Task<OfferPriceResponse?> PriceAsync(
            OfferPriceRequest request, Guid companyId, bool debug = false, CancellationToken ct = default)
        {
            var offer = await _offers.GetByIdAsync(request.OfferId, ct);
            if (offer is null || (offer.CompanyId is { } oc && oc != companyId))
                return null;

            var currency = request.Currency ?? offer.Currency ?? "AED";
            var response = new OfferPriceResponse
            {
                OfferId = offer.Id,
                Currency = currency,
                OriginDestinations = offer.OriginDestinations,
                PricingSummary = offer.PricingSummary,
                RuleId = offer.RuleId,
                RuleVersion = offer.RuleVersion,
            };

            var payload = new
            {
                offerId = offer.Id,
                currency,
                passengers = offer.Passengers.Select(p => new { id = p.Id, type = p.Type, age = p.Age }).ToList(),
                originDestinations = offer.OriginDestinations.Select(od => new
                {
                    origin = od.Origin,
                    destination = od.Destination,
                    bundles = od.FlightSolutions
                        .SelectMany(fs => fs.Bundles ?? new())
                        .Select(b => new { id = b.Id, code = b.BundleCode, total = b.Price?.Total })
                        .ToList(),
                }).ToList(),
            };

            var outcome = await _decisions.RunAsync(DecisionPoint.OfferPricing, payload, debug, ct);
            if (outcome.Warning is not null) response.Warnings.Add(outcome.Warning);

            if (outcome.Applied && outcome.Envelope?.Result is { } result)
            {
                ApplyRepricing(result, response, currency);
                response.RuleId = outcome.Envelope.RuleId;
                response.RuleVersion = outcome.Envelope.RuleVersion;
            }
            else
            {
                _log.LogInformation(
                    "OfferPricing degraded for offer {OfferId}; returning as-shopped prices.", offer.Id);
            }

            return response;
        }

        /// <summary>Overlay a pricing rule's <c>{total, currency}</c> result onto
        /// the summary if it supplied one. Kept deliberately small — the demo
        /// pricing rule is a stub; the full per-bundle re-price lands with the
        /// order module in a later phase.</summary>
        private static void ApplyRepricing(JsonElement result, OfferPriceResponse response, string currency)
        {
            if (result.ValueKind != JsonValueKind.Object) return;
            if (result.TryGetProperty("total", out var total) &&
                total.ValueKind == JsonValueKind.Number && total.TryGetDecimal(out var t))
                response.PricingSummary.Total = t;
            if (result.TryGetProperty("currency", out var cur) && cur.ValueKind == JsonValueKind.String)
                response.PricingSummary.Currency = cur.GetString() ?? currency;
        }
    }
}
