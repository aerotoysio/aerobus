using System.Security.Claims;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Distribution;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Offer
{
    public static class OfferEndpoints
    {
        public static RouteGroupBuilder OfferMapping(this RouteGroupBuilder group)
        {
            // Shop: search flights per O&D and price fare bundles via the RuleForge
            // ShopBundles decision. Never 500s if RuleForge is down — solutions
            // return with empty bundles and a warnings[] entry.
            group.MapPost("/shop", async (
                [FromBody] OfferShopRequest request,
                [FromServices] OfferShopService svc,
                ClaimsPrincipal user,
                HttpRequest http,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var debug = http.Query.ContainsKey("debug");
                var response = await svc.ShopAsync(request, companyId, debug, ct);
                return Results.Ok(response);
            });

            // Options (à la carte): after a Right to Fly is selected on a
            // shopped offer, the same connected policies run with mode=optional
            // and return the PRICED extras (Shape 3 of rule-based-retailing).
            group.MapPost("/options", async (
                [FromBody] OfferOptionsRequest request,
                [FromServices] OfferOptionsService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                try
                {
                    var result = await svc.GetOptionsAsync(
                        companyId, request.OfferId, request.FlightSolutionId, request.RtfCode ?? "", ct);
                    return Results.Ok(result);
                }
                catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            });

            // Price: re-price a previously shopped offer via the OfferPricing
            // decision. Keeps the ooms /offer/price route (which was a stub).
            group.MapPost("/price", async (
                [FromBody] OfferPriceRequest request,
                [FromServices] OfferPriceService svc,
                ClaimsPrincipal user,
                HttpRequest http,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var debug = http.Query.ContainsKey("debug");
                var response = await svc.PriceAsync(request, companyId, debug, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            });

            return group;
        }
    }
}
