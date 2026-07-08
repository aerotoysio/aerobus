using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class LayoutsEndpoints
    {
        public static RouteGroupBuilder CatalogueLayoutsMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (
                Guid id,
                [FromServices] LayoutsService svc,
                ClaimsPrincipal user) =>
            {
                var item = await svc.GetByIdAsync(id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

            // Main LIST – paged + search + status
            group.MapGet("/", async (
                [FromServices] LayoutsService svc,
                ClaimsPrincipal user,
                [FromQuery] string? status,
                [FromQuery] string? search,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var companyId = user.GetCompanyId();
                var page = pageNumber.GetValueOrDefault(1);
                var size = pageSize.GetValueOrDefault(50);

                var items = await svc.ListByCompanyAsync(companyId, status, search, page, size);
                return Results.Ok(items);
            });

            // Non-paged by company
            group.MapGet("/all", async (
                [FromServices] LayoutsService svc,
                ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.GetByCompanyAsync(companyId);
                return Results.Ok(items);
            });

            // Non-paged by equipment
            group.MapGet("/by-equipment/{equipmentId:guid}", async (
                Guid equipmentId,
                [FromServices] LayoutsService svc,
                ClaimsPrincipal user) =>
            {
                var items = await svc.GetByEquipmentAsync(equipmentId);
                return Results.Ok(items);
            });

            group.MapPost("/save", async (
                [FromBody] Layout layout,
                [FromServices] LayoutsService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    var saved = await svc.SaveAsync(layout);
                    return Results.Ok(saved);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            group.MapDelete("/{id:guid}", async (
                Guid id,
                [FromServices] LayoutsService svc,
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
