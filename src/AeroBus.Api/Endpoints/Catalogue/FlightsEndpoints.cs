using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class FlightsEndpoints
    {
        public static RouteGroupBuilder CatalogueFlightsMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (
                Guid id,
                [FromServices] FlightsService svc,
                ClaimsPrincipal user) =>
            {
                var item = await svc.GetByIdAsync(id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

            // Main list – paged + filters + search
            group.MapGet("/", async (
                [FromServices] FlightsService svc,
                ClaimsPrincipal user,
                [FromQuery] string? status,
                [FromQuery] string? departureStation,
                [FromQuery] string? arrivalStation,
                [FromQuery] string? search,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var companyId = user.GetCompanyId();
                var page = pageNumber.GetValueOrDefault(1);
                var size = pageSize.GetValueOrDefault(50);

                var result = await svc.ListByCompanyPagedAsync(
                    companyId,
                    status,
                    departureStation,
                    arrivalStation,
                    search,
                    page,
                    size);

                return Results.Ok(result);
            });

            // Non-paged "all by company"
            group.MapGet("/all", async (
                [FromServices] FlightsService svc,
                ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.GetByCompanyAsync(companyId);
                return Results.Ok(items);
            });

            // Range search - local time
            group.MapGet("/range/local", async (
                [FromServices] FlightsService svc,
                ClaimsPrincipal user,
                [FromQuery] string departureStation,
                [FromQuery] string arrivalStation,
                [FromQuery] DateTime fromLocal,
                [FromQuery] DateTime toLocal) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.FindByLocalRangeAsync(companyId, departureStation, arrivalStation, fromLocal, toLocal);
                return Results.Ok(items);
            });

            // Range search - UTC time
            group.MapGet("/range/utc", async (
                [FromServices] FlightsService svc,
                ClaimsPrincipal user,
                [FromQuery] string departureStation,
                [FromQuery] string arrivalStation,
                [FromQuery] DateTime fromUtc,
                [FromQuery] DateTime toUtc) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.FindByUtcRangeAsync(companyId, departureStation, arrivalStation, fromUtc, toUtc);
                return Results.Ok(items);
            });

            group.MapPost("/save", async (
                [FromBody] Flight f,
                [FromServices] FlightsService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    var saved = await svc.SaveAsync(f);
                    return Results.Ok(saved);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            group.MapDelete("/{id:guid}", async (
                Guid id,
                [FromQuery] Guid concurrencyId,
                [FromServices] FlightsService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    _ = await svc.DeleteAsync(id, concurrencyId);
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
