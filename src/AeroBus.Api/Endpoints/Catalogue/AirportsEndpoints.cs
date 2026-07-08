using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class AirportsEndpoints
    {
        public static RouteGroupBuilder CatalogueAirportsMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (Guid id, [FromServices] AirportService svc, ClaimsPrincipal user) =>
            {
                var x = await svc.GetByIdAsync(id);
                return x is null ? Results.NotFound() : Results.Ok(x);
            });

            // Main list endpoint – paged + search by company
            group.MapGet("/", async (
                [FromServices] AirportService svc,
                ClaimsPrincipal user,
                [FromQuery] string? search,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var companyId = user.GetCompanyId();
                var page = pageNumber.GetValueOrDefault(1);
                var size = pageSize.GetValueOrDefault(50);

                var items = await svc.ListByCompanyAsync(companyId, search, page, size);
                return Results.Ok(items);
            });

            // Non-paged "all" by company
            group.MapGet("/all", async ([FromServices] AirportService svc, ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var x = await svc.GetByCompanyAsync(companyId);
                return x is null ? Results.NotFound() : Results.Ok(x);
            });

            group.MapPost("/save", async ([FromBody] Airport a, [FromServices] AirportService svc, ClaimsPrincipal user) =>
            {
                try
                {
                    return Results.Ok(await svc.SaveAsync(a));
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            group.MapDelete("/{id:guid}", async (Guid id, [FromServices] AirportService svc, ClaimsPrincipal user, [FromQuery] Guid? concurrencyId = null) =>
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
