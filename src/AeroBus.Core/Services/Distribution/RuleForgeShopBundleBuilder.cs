using System.Text.Json;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Rules;
using Microsoft.Extensions.Logging;
using ShoppingFlightSolution = AeroBus.Core.Model.Shopping.FlightSolution;

namespace AeroBus.Core.Services.Distribution
{
    /// <summary>
    /// <see cref="IShopBundleBuilder"/> backed by the RuleForge ShopBundles
    /// decision point. For each flight solution it preloads the company's bundles
    /// and products, POSTs a <c>{searchContext, flightSolution, passengers,
    /// bundles, products}</c> payload, and maps the envelope's
    /// <c>result.bundles[]</c> onto the shop response shapes. When the decision
    /// skips or degrades (RuleForge down), it returns EMPTY bundles plus a
    /// warning — the shop never fails because the rules engine is unavailable.
    /// </summary>
    public sealed class RuleForgeShopBundleBuilder : IShopBundleBuilder
    {
        private readonly DecisionRunner _decisions;
        private readonly IBundles _bundles;
        private readonly IProducts _products;
        private readonly ILogger<RuleForgeShopBundleBuilder> _log;

        public RuleForgeShopBundleBuilder(
            DecisionRunner decisions,
            IBundles bundles,
            IProducts products,
            ILogger<RuleForgeShopBundleBuilder> log)
        {
            _decisions = decisions;
            _bundles = bundles;
            _products = products;
            _log = log;
        }

        public async Task<ShopBundleResult> BuildAsync(
            Guid companyId,
            IReadOnlyList<OfferShopPassenger> passengers,
            ShoppingFlightSolution flightSolution,
            string origin,
            string destination,
            string currency,
            bool debug = false,
            CancellationToken ct = default)
        {
            // Preload the company merchandising catalogue. These are the candidate
            // bundles/products the rule chooses from and prices; the rule echoes
            // back the eligible ones with computed prices.
            var companyBundles = await _bundles.GetByCompanyAsync(companyId, ct);
            var companyProducts = await _products.GetByCompanyAsync(companyId, ct);

            var payload = new
            {
                searchContext = new { currency, origin, destination },
                flightSolution = new
                {
                    id = flightSolution.Id,
                    origin,
                    destination,
                    cabin = flightSolution.Cabin,
                    elapsedDurationMinutes = flightSolution.ElapsedDurationMinutes,
                    legs = (flightSolution.Flights ?? new()).Select(f => new
                    {
                        flightRef = f.FlightRef,
                        marketingCarrier = f.MarketingCarrier,
                        from = f.Departure?.Airport,
                        to = f.Arrival?.Airport,
                    }).ToList(),
                },
                passengers = passengers.Select(p => new
                {
                    id = p.Id,
                    type = p.Type,
                    age = p.Age,
                }).ToList(),
                // Flat list of pax ids so a rule can echo the whole array in one
                // placeholder (the JSONPath subset's `from` resolves to the first
                // match only, so `$.passengers[*].id` can't rebuild the array).
                paxIds = passengers.Select(p => p.Id).ToList(),
                bundles = companyBundles.Select(b => new
                {
                    id = b.Id,
                    code = b.Type,
                    name = b.Name,
                    description = b.Description,
                    category = b.Category,
                    products = b.Products.Select(p => new { id = p.Id, code = p.Code, name = p.Name }).ToList(),
                }).ToList(),
                products = companyProducts.Select(p => new
                {
                    id = p.Id,
                    code = p.Code,
                    name = p.Name,
                    cost = p.CostAmount,
                    currency = p.CostCurrency,
                }).ToList(),
            };

            var outcome = await _decisions.RunAsync(DecisionPoint.ShopBundles, payload, debug, ct);

            if (!outcome.Applied || outcome.Envelope?.Result is not { } result)
            {
                // Degraded / skipped — return solutions with empty bundles.
                var warning = outcome.Warning
                              ?? $"ShopBundles produced no result for {origin}-{destination}; returning empty bundles.";
                _log.LogWarning("Shop bundles degraded for {Origin}-{Destination}: {Warning}", origin, destination, warning);
                return new ShopBundleResult(new List<ShopBundle>(), null, null, warning);
            }

            var bundles = MapBundles(result, currency);
            return new ShopBundleResult(bundles, outcome.Envelope.RuleId, outcome.Envelope.RuleVersion, Warning: null);
        }

        /// <summary>
        /// Map a RuleForge ShopBundles result onto <see cref="ShopBundle"/>s. The
        /// result is either an object with a <c>bundles</c> array, or a bare array
        /// of bundle objects (the rule's output node may emit either).
        /// </summary>
        private static List<ShopBundle> MapBundles(JsonElement result, string currency)
        {
            var arr = result switch
            {
                { ValueKind: JsonValueKind.Array } => result,
                { ValueKind: JsonValueKind.Object } when result.TryGetProperty("bundles", out var b) && b.ValueKind == JsonValueKind.Array => b,
                _ => default,
            };
            if (arr.ValueKind != JsonValueKind.Array) return new List<ShopBundle>();

            var bundles = new List<ShopBundle>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                bundles.Add(MapBundle(el, currency));
            }
            return bundles;
        }

        private static ShopBundle MapBundle(JsonElement el, string currency)
        {
            var bundle = new ShopBundle
            {
                Id = GetGuid(el, "bundleId") ?? GetGuid(el, "id") ?? Guid.Empty,
                BundleCode = GetString(el, "code") ?? GetString(el, "bundleCode"),
                Name = GetString(el, "name"),
                Description = GetString(el, "description"),
                EligiblePaxIds = GetGuidList(el, "eligiblePaxIds"),
            };

            if (el.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object)
            {
                bundle.Price = new BundlePrice
                {
                    Currency = GetString(price, "currency") ?? currency,
                    Base = GetDecimal(price, "base"),
                    Taxes = GetDecimal(price, "taxes"),
                    Total = GetDecimal(price, "total"),
                    Components = MapComponents(price),
                };
            }
            else if (el.TryGetProperty("total", out _) || el.TryGetProperty("base", out _))
            {
                // Flat pricing: the rule emitted base/taxes/total as top-level
                // fields (RuleForge calc/mutator targets are flat keys). Fold them
                // into the price object and synthesise base/tax components.
                var baseAmount = GetDecimal(el, "base");
                var taxes = GetDecimal(el, "taxes");
                bundle.Price = new BundlePrice
                {
                    Currency = GetString(el, "currency") ?? currency,
                    Base = baseAmount,
                    Taxes = taxes,
                    Total = GetDecimal(el, "total"),
                    Components = new List<PriceComponent>
                    {
                        new() { Code = "FARE", Type = "BASE", Amount = baseAmount },
                        new() { Code = "TAX", Type = "TAX", Amount = taxes },
                    },
                };
            }

            if (el.TryGetProperty("services", out var services) && services.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in services.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    bundle.Services.Add(new BundleServiceItem
                    {
                        Id = GetGuid(s, "id") ?? Guid.Empty,
                        Code = GetString(s, "code"),
                        Name = GetString(s, "name"),
                        Description = GetString(s, "description"),
                        Included = s.TryGetProperty("included", out var inc) && inc.ValueKind == JsonValueKind.True,
                        EligiblePaxIds = GetGuidList(s, "eligiblePaxIds"),
                    });
                }
            }

            return bundle;
        }

        private static List<PriceComponent> MapComponents(JsonElement price)
        {
            var list = new List<PriceComponent>();
            if (price.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in comps.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new PriceComponent
                    {
                        Code = GetString(c, "code"),
                        Type = GetString(c, "type"),
                        Amount = GetDecimal(c, "amount"),
                    });
                }
            }
            return list;
        }

        // ─── JSON helpers (tolerant of string/number and missing fields) ──────

        private static string? GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static Guid? GetGuid(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)
                ? g : null;

        private static List<Guid> GetGuidList(JsonElement el, string name)
        {
            var list = new List<Guid>();
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var item in v.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var g))
                        list.Add(g);
            return list;
        }

        private static decimal GetDecimal(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return 0m;
            return v.ValueKind switch
            {
                JsonValueKind.Number when v.TryGetDecimal(out var d) => d,
                JsonValueKind.String when decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
                _ => 0m,
            };
        }
    }
}
