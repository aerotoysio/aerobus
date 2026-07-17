using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class BundlesEndpoints
    {
        public static RouteGroupBuilder CatalogueBundlesMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (Guid id, BundleService svc, ClaimsPrincipal user) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/company/{companyId:guid}", async (Guid companyId, BundleService svc, ClaimsPrincipal user) =>
                Results.Ok(await svc.GetByCompanyAsync(companyId)));

            group.MapGet("/company/{companyId:guid}/pretty", async (Guid companyId, BundleService svc, ClaimsPrincipal user) =>
                Results.Ok(await svc.GetPrettyByCompanyAsync(companyId)));

            // Search with paging (phase 1; no TotalCount)
            // Example:
            // GET /search?companyId=...&q=bag&status=Active&type=Ancillary&category=Gold&pageNumber=1&pageSize=50
            group.MapGet("/search", async (
                [FromQuery] Guid? companyId,
                [FromQuery(Name = "q")] string? search,
                [FromQuery] string? status,
                [FromQuery] string? type,
                [FromQuery] string? category,
                [FromQuery] int pageNumber,
                [FromQuery] int pageSize,
                BundleService svc,
                ClaimsPrincipal user) =>
            {
                if (pageNumber <= 0) pageNumber = 1;
                if (pageSize <= 0) pageSize = 50;

                var items = await svc.SearchAsync(companyId, search, status, type, category, pageNumber, pageSize);
                return Results.Ok(items);
            });

            group.MapPost("/save", async ([FromBody] Bundle m, [FromServices] BundleService svc, ClaimsPrincipal user) =>
            {
                try
                {
                    m = m with { CompanyId = user.ResolveCompanyId(m.CompanyId) };
                    return Results.Ok(await svc.SaveAsync(m));
                }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            group.MapDelete("/{id:guid}", async (Guid id, BundleService svc, ClaimsPrincipal user, [FromQuery] Guid? concurrencyId = null) =>
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
