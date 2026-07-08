using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class SchedulesEndpoints
    {
        public static RouteGroupBuilder CatalogueSchedulesMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (
                Guid id,
                [FromServices] SchedulesService svc,
                ClaimsPrincipal user) =>
            {
                var item = await svc.GetByIdAsync(id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

            // Main LIST – paged + filters + search
            group.MapGet("/", async (
                [FromServices] SchedulesService svc,
                ClaimsPrincipal user,
                [FromQuery] string? status,
                [FromQuery] string? carrierCode,
                [FromQuery] string? departureStation,
                [FromQuery] string? arrivalStation,
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
                    carrierCode,
                    departureStation,
                    arrivalStation,
                    search,
                    page,
                    size);

                return Results.Ok(items);
            });

            // Non-paged "all by company"
            group.MapGet("/all", async (
                [FromServices] SchedulesService svc,
                ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.GetByCompanyAsync(companyId);
                return Results.Ok(items);
            });

            group.MapPost("/save", async (
                [FromBody] Schedule schedule,
                [FromServices] SchedulesService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    var saved = await svc.SaveAsync(schedule);
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
                [FromServices] SchedulesService svc,
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
