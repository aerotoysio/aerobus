using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class MarketZonesEndpoints
    {
        public static RouteGroupBuilder CatalogueMarketZonesMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (Guid id, [FromServices] MarketZoneService svc) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/company/{companyId:guid}", async (Guid companyId, [FromServices] MarketZoneService svc) =>
                Results.Ok(await svc.GetByCompanyAsync(companyId)));

            group.MapGet("/search", async (
                [FromQuery] Guid? companyId,
                [FromQuery] string? status,
                [FromQuery] string? search,
                [FromQuery] int pageNumber,
                [FromQuery] int pageSize,
                [FromServices] MarketZoneService svc) =>
            {
                var items = await svc.SearchAsync(companyId, status, search, pageNumber, pageSize);
                return Results.Ok(items);
            });

            group.MapPost("/save", async ([FromBody] MarketZone m, [FromServices] MarketZoneService svc) =>
            {
                try { return Results.Ok(await svc.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            group.MapPost("/build/{id:guid}", async (Guid id, [FromServices] MarketZoneService svc) =>
            {
                try { return Results.Ok(await svc.BuildAsync(id)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            group.MapDelete("/{id:guid}", async (Guid id, [FromServices] MarketZoneService svc, [FromQuery] Guid? concurrencyId = null) =>
            {
                try
                {
                    var ok = await svc.DeleteAsync(id, concurrencyId ?? Guid.Empty);
                    return ok ? Results.NoContent() : Results.Conflict("Concurrency violation or not found.");
                }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            return group;
        }
    }
}
