using System.Diagnostics;
using AeroBus.Core.Common.Cache;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Repositories.Distribution;
using AeroBus.Core.Repositories.Shopping;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>
    /// Offer shopping runtime, ported from the ooms OfferShopService. Keeps the
    /// /offer/shop contract and the O&amp;D + direct/connecting flight-solution
    /// search, but replaces the dropped ooms PricingEngine + node-handler offer
    /// engine with a RuleForge ShopBundles decision (via <see cref="IShopBundleBuilder"/>).
    /// Each shopped offer is persisted to the <c>offers</c> collection; the ooms
    /// Redis offer cache is intentionally not ported.
    /// </summary>
    public sealed class OfferShopService
    {
        private readonly IDirectFlightSolutions _directFlightSolutions;
        private readonly IShopBundleBuilder _shopBundleBuilder;
        private readonly IOffers _offers;
        private readonly IFlights _flights;
        private readonly IHotCache _cache;
        private readonly ILogger<OfferShopService> _log;

        public OfferShopService(
            IDirectFlightSolutions directFlightSolutions,
            IShopBundleBuilder shopBundleBuilder,
            IOffers offers,
            IFlights flights,
            IHotCache cache,
            ILogger<OfferShopService> log)
        {
            _directFlightSolutions = directFlightSolutions;
            _shopBundleBuilder = shopBundleBuilder;
            _offers = offers;
            _flights = flights;
            _cache = cache;
            _log = log;
        }

        public async Task<OfferShopResponse> ShopAsync(
            OfferShopRequest request, Guid companyId, bool debug = false, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            // The flight-solution engine searches the CacheKeys.Flights hot bucket,
            // which has no request-path loader in AeroBus (a Phase 3 gap: the shop
            // was never wired end-to-end). Warm it from the company's flights before
            // searching so /offer/shop finds solutions. Idempotent — reloads each
            // shop so a freshly-built schedule is visible.
            await WarmFlightCacheAsync(companyId, ct);

            var currency = request.SearchContext?.Currency ?? "AED";
            var passengers = request.Passengers ?? new List<OfferShopPassenger>();

            var response = new OfferShopResponse
            {
                SearchId = Guid.NewGuid(),
                Channel = request.SearchContext?.Channel,
                Currency = currency,
                Passengers = passengers,
                OriginDestinations = new List<OriginDestinationResponse>(),
                PricingSummary = new PricingSummary { Currency = currency, Total = 0m, PerPaxType = new() },
            };

            string? ruleId = null;
            int? ruleVersion = null;

            if (request.SearchCriteria?.OriginDestinations is { } odRequests)
            {
                // null/unset => default to a single connection so connecting itineraries surface;
                // an explicit value is honoured, including 0 for direct-only.
                var maxStops = Math.Max(0, request.SearchCriteria.MaxConnections ?? 1);
                var maxPerOd = request.SearchCriteria.MaxResultsPerOD > 0 ? request.SearchCriteria.MaxResultsPerOD : 20;

                foreach (var odReq in odRequests)
                {
                    var origin = odReq.Origin ?? string.Empty;
                    var destination = odReq.Destination ?? string.Empty;

                    var odResp = new OriginDestinationResponse
                    {
                        Id = Guid.NewGuid(),
                        OdRef = odReq.OdRef,
                        Origin = origin,
                        Destination = destination,
                        DepartureDate = odReq.DepartureDate,
                        FlightSolutions = new(),
                    };

                    var solutions = await _directFlightSolutions.SearchAsync(
                        origin, destination, odReq.DepartureDate, maxStops, maxPerOd, ct);

                    foreach (var solution in solutions)
                    {
                        solution.Id = solution.Id == Guid.Empty ? Guid.NewGuid() : solution.Id;

                        var built = await _shopBundleBuilder.BuildAsync(
                            companyId, passengers, solution, origin, destination, currency, debug, ct);

                        solution.Bundles = built.Bundles;
                        if (built.RuleId is not null) { ruleId = built.RuleId; ruleVersion = built.RuleVersion; }
                        if (built.Warning is not null) response.Warnings.Add(built.Warning);
                    }

                    odResp.FlightSolutions.AddRange(solutions);
                    response.OriginDestinations.Add(odResp);
                }
            }

            BuildPricingSummary(response);

            // Persist the shopped offer so a later order-create can bind against
            // exactly what was shopped. events: offer.created via outbox in Phase 6.
            await PersistOfferAsync(response, companyId, ruleId, ruleVersion, ct);

            response.ResponseTime = (int)sw.ElapsedMilliseconds;
            return response;
        }

        private async Task WarmFlightCacheAsync(Guid companyId, CancellationToken ct)
        {
            try
            {
                var flights = await _flights.GetByCompanyAsync(companyId, ct);
                _cache.Set(CacheKeys.Flights, flights);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to warm flight cache for company {CompanyId}; shop may find no solutions.", companyId);
            }
        }

        private static void BuildPricingSummary(OfferShopResponse response)
        {
            decimal grandTotal = 0m;
            foreach (var od in response.OriginDestinations)
            {
                foreach (var fs in od.FlightSolutions)
                {
                    var cheapest = fs.Bundles?
                        .Where(b => b.Price is not null)
                        .OrderBy(b => b.Price!.Total)
                        .FirstOrDefault();
                    if (cheapest?.Price is not null) grandTotal += cheapest.Price.Total;
                }
            }

            response.PricingSummary.Total = grandTotal;

            if (response.Passengers.Count > 0)
            {
                var grouped = response.Passengers.GroupBy(p => p.Type).ToList();
                foreach (var g in grouped)
                    response.PricingSummary.PerPaxType.Add(new PaxPriceSummary
                    {
                        Type = g.Key,
                        Count = g.Count(),
                        Total = grouped.Count == 0 ? 0m : grandTotal / grouped.Count,
                    });
            }
        }

        private async Task PersistOfferAsync(
            OfferShopResponse response, Guid companyId, string? ruleId, int? ruleVersion, CancellationToken ct)
        {
            try
            {
                var now = DateTime.UtcNow;
                var offer = new Offer
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    SearchId = response.SearchId,
                    Channel = response.Channel,
                    Currency = response.Currency,
                    Passengers = response.Passengers,
                    OriginDestinations = response.OriginDestinations,
                    PricingSummary = response.PricingSummary,
                    RuleId = ruleId,
                    RuleVersion = ruleVersion,
                    ExpiresAt = now.AddMinutes(30),
                    Created = now,
                    Updated = now,
                    Status = "Shopped",
                };
                await _offers.SaveAsync(offer, ct);
                _offerIdBySearch = offer.Id;
            }
            catch (Exception ex)
            {
                // Persisting the offer must not fail the shop — the search + bundles
                // are already computed and returned to the caller.
                _log.LogWarning(ex, "Failed to persist shopped offer for search {SearchId}", response.SearchId);
            }
        }

        // Surfaces the persisted offer id from the most recent shop (used by the
        // endpoint layer / tests to look the offer document up). Scoped-per-request
        // service, so this is safe.
        private Guid _offerIdBySearch;

        /// <summary>The <c>offers</c> document id created by the last
        /// <see cref="ShopAsync"/> call on this (scoped) instance.</summary>
        public Guid LastPersistedOfferId => _offerIdBySearch;
    }
}
