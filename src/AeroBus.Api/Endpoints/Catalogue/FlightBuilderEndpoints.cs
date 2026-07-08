using System.Security.Claims;
using AeroBus.Core.Services.Catalogue;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Catalogue
{
    public static class FlightBuilderEndpoints
    {
        public static RouteGroupBuilder CatalogueFlightBuilderMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/preview/{scheduleId:guid}", async (Guid scheduleId, [FromServices] FlightBuilderService svc, ClaimsPrincipal user)
                => Results.Ok(await svc.PreviewAsync(scheduleId)));

            group.MapPost("/build/{scheduleId:guid}", async (Guid scheduleId, [FromServices] FlightBuilderService svc, ClaimsPrincipal user) =>
            {
                var n = await svc.BuildAsync(scheduleId);
                return Results.Ok(new { built = n });
            });

            return group;
        }
    }
}
