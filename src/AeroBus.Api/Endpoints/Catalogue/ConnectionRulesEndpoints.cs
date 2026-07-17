using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class ConnectionRulesEndpoints
    {
        public static RouteGroupBuilder CatalogueConnectionRulesMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (
                Guid id,
                [FromServices] ConnectionRulesService svc,
                ClaimsPrincipal user) =>
            {
                var rule = await svc.GetByIdAsync(id);
                return rule is null ? Results.NotFound() : Results.Ok(rule);
            });

            // Main LIST endpoint (paged + search)
            group.MapGet("/", async (
                [FromServices] ConnectionRulesService svc,
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

            // Non-paged "all" for debugging / specific use
            group.MapGet("/all", async (
                [FromServices] ConnectionRulesService svc,
                ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.GetByCompanyAsync(companyId);
                return Results.Ok(items);
            });

            group.MapPost("/save", async (
                [FromBody] ConnectionRule rule,
                [FromServices] ConnectionRulesService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    rule = rule with { CompanyId = user.ResolveCompanyId(rule.CompanyId) };
                    var saved = await svc.SaveAsync(rule);
                    return Results.Ok(saved);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            group.MapDelete("/{id:guid}", async (
                Guid id,
                [FromServices] ConnectionRulesService svc,
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
