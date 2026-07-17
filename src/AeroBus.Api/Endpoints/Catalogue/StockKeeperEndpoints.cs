using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class StockKeeperEndpoints
    {
        public static RouteGroupBuilder CatalogueStockKeeperMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (
                Guid id,
                [FromServices] StockKeeperService svc,
                ClaimsPrincipal user) =>
            {
                var item = await svc.GetByIdAsync(id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

            // Main LIST – paged + filters + search
            group.MapGet("/", async (
                [FromServices] StockKeeperService svc,
                ClaimsPrincipal user,
                [FromQuery] string? status,
                [FromQuery] string? category,
                [FromQuery] string? type,
                [FromQuery] string? scope,
                [FromQuery] string? search,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var companyId = user.GetCompanyId();
                var page = pageNumber.GetValueOrDefault(1);
                var size = pageSize.GetValueOrDefault(50);

                var items = await svc.ListByCompanyAsync(
                    companyId,
                    status,
                    category,
                    type,
                    scope,
                    search,
                    page,
                    size);

                return Results.Ok(items);
            });

            // Non-paged cache load
            group.MapGet("/all", async (
                [FromServices] StockKeeperService svc,
                ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.GetByCompanyAsync(companyId);
                return Results.Ok(items);
            });

            group.MapPost("/save", async (
                [FromBody] StockKeeper sk,
                [FromServices] StockKeeperService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    sk = sk with { CompanyId = user.ResolveCompanyId(sk.CompanyId) };
                    var saved = await svc.SaveAsync(sk);
                    return Results.Ok(saved);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            group.MapDelete("/{id:guid}", async (
                Guid id,
                [FromServices] StockKeeperService svc,
                ClaimsPrincipal user,
                [FromQuery] Guid? concurrencyId = null) =>
            {
                try
                {
                    _ = await svc.DeleteAsync(id, concurrencyId ?? Guid.Empty);
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            return group;
        }
    }
}
