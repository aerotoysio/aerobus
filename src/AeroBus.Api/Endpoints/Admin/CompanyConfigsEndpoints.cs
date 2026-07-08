using System.Security.Claims;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public static class CompanyConfigsEndpoints
    {
        public static RouteGroupBuilder AdminCompanyConfigMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id}", async (Guid id, [FromServices] CompanyConfigService svc, ClaimsPrincipal user) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapGet("/company/{companyId}", async (Guid companyId, [FromServices] CompanyConfigService svc, ClaimsPrincipal user) =>
                (await svc.GetByCompanyAsync(companyId)) is { } x ? Results.Ok(x) : Results.NotFound());

            group.MapPost("/save", async ([FromBody] CompanyConfig m, [FromServices] CompanyConfigService svc, ClaimsPrincipal user) =>
            {
                try { return Results.Ok(await svc.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            group.MapDelete("/{companyId:guid}/{key}", async (Guid companyId, string key, [FromServices] CompanyConfigService svc, ClaimsPrincipal user) =>
            {
                try { _ = await svc.DeleteAsync(companyId, key); return Results.NoContent(); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            });

            return group;
        }
    }
}
