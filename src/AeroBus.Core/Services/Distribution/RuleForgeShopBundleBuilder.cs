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
        private readonly IRightsToFly _rightsToFly;
        private readonly Rules.RuleAuthoringService _authoring;
        private readonly IRuleForgeClient _ruleForge;
        private readonly ILogger<RuleForgeShopBundleBuilder> _log;

        // Rule id → endpoint, cached for the request (eligibility + policy
        // rules are looked up once however many solutions the shop returns).
        private readonly Dictionary<string, string?> _endpointCache = new(StringComparer.Ordinal);

        public RuleForgeShopBundleBuilder(
            DecisionRunner decisions,
            IBundles bundles,
            IProducts products,
            IRightsToFly rightsToFly,
            Rules.RuleAuthoringService authoring,
            IRuleForgeClient ruleForge,
            ILogger<RuleForgeShopBundleBuilder> log)
        {
            _decisions = decisions;
            _bundles = bundles;
            _products = products;
            _rightsToFly = rightsToFly;
            _authoring = authoring;
            _ruleForge = ruleForge;
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
            // Right to Fly is the brand row when the airline has configured any
            // (docs/rule-based-retailing.md); the catalogue bundles remain the
            // candidates for airlines that haven't migrated.
            var rtfs = (await _rightsToFly.GetByCompanyAsync(companyId, ct))
                .Where(r => string.Equals(r.Status ?? "Active", "Active", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Rank)
                .ToList();

            // Eligibility gate: an RTF with an eligibility rule is only offered
            // when that rule applies. Degraded/unbound rules fail OPEN (brand
            // offered) — the shop never loses a brand to an engine hiccup.
            if (rtfs.Count > 0)
                rtfs = await FilterEligibleAsync(rtfs, passengers, flightSolution, origin, destination, currency, ct);

            var companyBundles = await _bundles.GetByCompanyAsync(companyId, ct);
            var companyProducts = await _products.GetByCompanyAsync(companyId, ct);

            var candidates = rtfs.Count > 0
                ? rtfs.Select(r => new
                {
                    id = r.Id,
                    code = r.RtfCode,
                    name = r.RtfName,
                    description = r.Description,
                    category = (string?)"rtf",
                    products = new List<object>(),
                }).Cast<object>().ToList()
                : companyBundles.Select(b => new
                {
                    id = b.Id,
                    code = b.Type,
                    name = b.Name,
                    description = b.Description,
                    category = b.Category,
                    products = b.Products.Select(p => new { id = p.Id, code = p.Code, name = p.Name }).Cast<object>().ToList(),
                }).Cast<object>().ToList();

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
                bundles = candidates,
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

            // Benefits: each brand's connected policies run with mode=included
            // and the results land on the bundle as included services — Saver +
            // Baggage Policy pops out hand luggage, Gold pops out the extra bag.
            if (rtfs.Count > 0)
                await AttachIncludedBenefitsAsync(rtfs, bundles, passengers, flightSolution, origin, destination, currency, ct);

            return new ShopBundleResult(bundles, outcome.Envelope.RuleId, outcome.Envelope.RuleVersion, Warning: null);
        }

        // ─── Right to Fly orchestration ───────────────────────────────────────

        private async Task<List<RightToFly>> FilterEligibleAsync(
            List<RightToFly> rtfs,
            IReadOnlyList<OfferShopPassenger> passengers,
            ShoppingFlightSolution flightSolution,
            string origin, string destination, string currency,
            CancellationToken ct)
        {
            var eligible = new List<RightToFly>(rtfs.Count);
            foreach (var rtf in rtfs)
            {
                if (string.IsNullOrWhiteSpace(rtf.EligibilityRuleId))
                {
                    eligible.Add(rtf);
                    continue;
                }
                var endpoint = await ResolveEndpointAsync(rtf.EligibilityRuleId, ct);
                if (endpoint is null) { eligible.Add(rtf); continue; }
                try
                {
                    var envelope = await _ruleForge.EvaluateAsync(
                        endpoint, ShapePayload("included", rtf, passengers, flightSolution, origin, destination, currency), ct: ct);
                    if (envelope.Decision != Decision.Skip) eligible.Add(rtf);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "RTF {Code} eligibility rule {RuleId} degraded; offering the brand (fail-open).",
                        rtf.RtfCode, rtf.EligibilityRuleId);
                    eligible.Add(rtf);
                }
            }
            return eligible;
        }

        private async Task AttachIncludedBenefitsAsync(
            List<RightToFly> rtfs,
            List<ShopBundle> bundles,
            IReadOnlyList<OfferShopPassenger> passengers,
            ShoppingFlightSolution flightSolution,
            string origin, string destination, string currency,
            CancellationToken ct)
        {
            foreach (var bundle in bundles)
            {
                var rtf = rtfs.FirstOrDefault(r =>
                    string.Equals(r.RtfCode, bundle.BundleCode, StringComparison.OrdinalIgnoreCase));
                if (rtf is null) continue;

                var items = await RunPoliciesAsync(rtf, "included", passengers, flightSolution, origin, destination, currency, ct);
                foreach (var item in items) bundle.Services.Add(item);
            }
        }

        /// <summary>
        /// Run every policy connected to the RTF with the given mode and map the
        /// product outputs to service items. Best-effort per policy: a degraded
        /// or unpublished policy grants nothing and logs, never fails the shop.
        /// </summary>
        internal async Task<List<BundleServiceItem>> RunPoliciesAsync(
            RightToFly rtf,
            string mode,
            IReadOnlyList<OfferShopPassenger> passengers,
            ShoppingFlightSolution flightSolution,
            string origin, string destination, string currency,
            CancellationToken ct)
        {
            var items = new List<BundleServiceItem>();
            var payload = await ToPolicyContractAsync(
                mode, rtf, passengers, flightSolution, origin, destination, currency, ct);
            foreach (var policyId in rtf.PolicyIds ?? [])
            {
                var endpoint = await ResolveEndpointAsync(policyId, ct);
                if (endpoint is null) continue;
                try
                {
                    var envelope = await _ruleForge.EvaluateAsync(endpoint, payload, ct: ct);
                    if (envelope.Decision != Decision.Apply || envelope.Result is not { } result) continue;
                    items.AddRange(MapPolicyProducts(result, included: mode == "included"));
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Policy {PolicyId} for RTF {Code} degraded ({Mode}); granting nothing from it.",
                        policyId, rtf.RtfCode, mode);
                }
            }
            return items;
        }

        private static IEnumerable<BundleServiceItem> MapPolicyProducts(JsonElement result, bool included)
        {
            var elements = result.ValueKind switch
            {
                JsonValueKind.Array => result.EnumerateArray().ToList(),
                JsonValueKind.Object => [result],
                _ => new List<JsonElement>(),
            };
            foreach (var el in elements)
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var code = GetString(el, "code") ?? GetString(el, "productCode");
                if (string.IsNullOrWhiteSpace(code)) continue;
                var price = GetDecimal(el, "price");
                if (price == 0m) price = GetDecimal(el, "total");
                var qty = (int)GetDecimal(el, "quantity");
                yield return new BundleServiceItem
                {
                    Id = GetGuid(el, "id") ?? Guid.Empty,
                    Code = code,
                    Name = GetString(el, "name"),
                    Description = GetString(el, "description") ?? GetString(el, "reason"),
                    Included = included,
                    Price = price > 0m ? price : null,
                    Quantity = qty > 0 ? qty : 1,
                };
            }
        }

        /// <summary>
        /// Build the caller-shape request and PROJECT it into the canonical
        /// policy contract (ShapeProjector) — one policy serves every shape.
        /// Falls back to the raw request when the shapes can't be loaded.
        /// </summary>
        private async Task<object> ToPolicyContractAsync(
            string mode,
            RightToFly rtf,
            IReadOnlyList<OfferShopPassenger> passengers,
            ShoppingFlightSolution flightSolution,
            string origin, string destination, string currency,
            CancellationToken ct)
        {
            var raw = ShapePayload(mode, rtf, passengers, flightSolution, origin, destination, currency);
            try
            {
                var shapeId = mode == "optional" ? "a-la-carte" : "rtf-benefits";
                var shape = await _authoring.GetShapeAsync(shapeId, ct);
                var canonical = await _authoring.GetShapeAsync("policy-input", ct);
                if (shape is null || canonical is null) return raw;

                var request = JsonSerializer.SerializeToElement(raw);
                var projected = Rules.ShapeProjector.Project(request, shape.Value, canonical.Value);

                // The contract's scalar passthroughs (mode, rightToFly) ride on
                // shape fields; keep them present even if a shape drops them.
                var node = System.Text.Json.Nodes.JsonNode.Parse(projected.GetRawText())!.AsObject();
                node["mode"] ??= mode;
                node["rightToFly"] ??= System.Text.Json.Nodes.JsonNode.Parse(
                    JsonSerializer.Serialize(new { code = rtf.RtfCode, name = rtf.RtfName }));
                return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Policy contract projection failed for {Mode}; sending the raw request.", mode);
                return raw;
            }
        }

        private object ShapePayload(
            string mode,
            RightToFly rtf,
            IReadOnlyList<OfferShopPassenger> passengers,
            ShoppingFlightSolution flightSolution,
            string origin, string destination, string currency) => new
        {
            mode,
            searchContext = new { currency, origin, destination },
            rightToFly = new { code = rtf.RtfCode, name = rtf.RtfName },
            customers = passengers.Select(p => new
            {
                id = p.Id,
                type = p.Type,
                age = p.Age,
                loyaltyTier = p.LoyaltyTier,
            }).ToList(),
            // Singular alias: filters authored as customer.<field> read the lead.
            customer = passengers.Select(p => new { loyaltyTier = p.LoyaltyTier, type = p.Type }).FirstOrDefault(),
            paxIds = passengers.Select(p => p.Id).ToList(),
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
        };

        private async Task<string?> ResolveEndpointAsync(string ruleId, CancellationToken ct)
        {
            if (_endpointCache.TryGetValue(ruleId, out var cached)) return cached;
            string? endpoint = null;
            try
            {
                var rule = await _authoring.GetRuleAsync(ruleId, ct);
                if (rule is { } r && r.TryGetProperty("endpoint", out var e) && e.ValueKind == JsonValueKind.String)
                    endpoint = e.GetString();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not resolve endpoint for rule {RuleId}.", ruleId);
            }
            _endpointCache[ruleId] = endpoint;
            return endpoint;
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
