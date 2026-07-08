using System.Security.Claims;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    public static class CompaniesEndpoints
    {
        public static RouteGroupBuilder AdminCompaniesMapping(this RouteGroupBuilder group)
        {
            // Retrieves a company by its unique CompanyId (GUID), including its
            // configs. Requires `company.view` permission.
            group.MapGet("/{id:guid}", async (Guid id, [FromServices] CompanyService svc, ClaimsPrincipal user) =>
                (await svc.GetByIdAsync(id)) is { } x ? Results.Ok(x) : Results.NotFound())
                .RequireAuthorization("company.view");

            // Retrieves a company by its slug (unique, URL-safe identifier).
            // Requires `company.view` permission.
            group.MapGet("/slug/{slug}", async (string slug, [FromServices] CompanyService svc, ClaimsPrincipal user) =>
                (await svc.GetBySlugAsync(slug)) is { } x ? Results.Ok(x) : Results.NotFound())
                .RequireAuthorization("company.view");

            // Returns all companies visible to the authenticated user.
            // Requires `company.list` permission.
            group.MapGet("/all", async ([FromServices] CompanyService svc, ClaimsPrincipal user) =>
                (await svc.GetAllAsync()) is { } x ? Results.Ok(x) : Results.NotFound())
                .RequireAuthorization("company.list");

            // Creates or updates a company record.
            // Requires `company.save` permission.
            group.MapPost("/save", async ([FromBody] Company m, [FromServices] CompanyService svc, ClaimsPrincipal user) =>
            {
                try { return Results.Ok(await svc.SaveAsync(m)); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            }).RequireAuthorization("company.save");

            // Deletes a company by its unique id.
            // Requires `company.delete` permission.
            group.MapDelete("/{id}", async (Guid id, [FromServices] CompanyService svc, ClaimsPrincipal user) =>
            {
                try { _ = await svc.DeleteAsync(id); return Results.NoContent(); }
                catch (Exception ex) { return Results.BadRequest(ex.Message); }
            }).RequireAuthorization("company.delete");

            return group;
        }
    }
}
