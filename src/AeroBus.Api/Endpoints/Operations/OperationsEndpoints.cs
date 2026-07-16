using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Operations;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AeroBus.Api.Endpoints.Operations
{
    public sealed record FlightStatusRequest(string Action);
    public sealed record CheckInRequest(Guid FlightId, Guid PassengerId, int? SeatRow, string? SeatColumn);
    public sealed record BoardRequest(Guid FlightId, Guid PassengerId);

    /// <summary>
    /// Departure control (DCS) — the operational surface a check-in/gate workstation
    /// (aeroboard) drives: a station's departures for a day, the flight status
    /// lifecycle (Scheduled → Boarding → Departed / Cancelled), the passenger
    /// manifest, and per-passenger check-in / boarding. Company-scoped (companyId
    /// from the caller's token). Reads require <c>operations.view</c>; writes require
    /// <c>operations.manage</c>.
    /// </summary>
    public static class OperationsEndpoints
    {
        private const string Manage = "operations.manage";

        public static RouteGroupBuilder OperationsMapping(this RouteGroupBuilder group)
        {
            // ── departures board ─────────────────────────────────────────────────
            group.MapGet("/departures", async (
                [FromQuery] string departureStation, [FromQuery] DateOnly? date,
                [FromServices] FlightOperationsService svc, ClaimsPrincipal user, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(departureStation))
                    return Results.BadRequest(new { error = "departureStation is required." });
                var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
                return Results.Ok(await svc.ListDeparturesAsync(user.GetCompanyId(), departureStation.ToUpperInvariant(), day, ct));
            });

            group.MapGet("/flights/{flightId:guid}", async (
                Guid flightId, [FromServices] FlightOperationsService svc, ClaimsPrincipal user, CancellationToken ct) =>
                (await svc.GetFlightAsync(user.GetCompanyId(), flightId, ct)) is { } f ? Results.Ok(f) : Results.NotFound());

            // ── flight status ────────────────────────────────────────────────────
            group.MapPost("/flights/{flightId:guid}/status", async (
                Guid flightId, [FromBody] FlightStatusRequest body,
                [FromServices] FlightOperationsService svc, ClaimsPrincipal user, CancellationToken ct) =>
                MapFlight(await svc.ChangeStatusAsync(user.GetCompanyId(), flightId, body.Action, ct))).RequireAuthorization(Manage);

            group.MapPost("/flights/{flightId:guid}/depart", async (
                Guid flightId, [FromServices] FlightOperationsService svc, ClaimsPrincipal user, CancellationToken ct) =>
                MapFlight(await svc.ChangeStatusAsync(user.GetCompanyId(), flightId, FlightStateMachine.Action.Depart, ct))).RequireAuthorization(Manage);

            // ── manifest + boarding ──────────────────────────────────────────────
            group.MapGet("/flights/{flightId:guid}/manifest", async (
                Guid flightId, [FromServices] CheckInService svc, ClaimsPrincipal user, CancellationToken ct) =>
                Results.Ok(await svc.GetManifestAsync(user.GetCompanyId(), flightId, ct)));

            group.MapPost("/checkin", async (
                [FromBody] CheckInRequest body, [FromServices] CheckInService svc, ClaimsPrincipal user, CancellationToken ct) =>
                MapCheckIn(await svc.CheckInAsync(user.GetCompanyId(), body.FlightId, body.PassengerId, body.SeatRow, body.SeatColumn, ct))).RequireAuthorization(Manage);

            group.MapPost("/board", async (
                [FromBody] BoardRequest body, [FromServices] CheckInService svc, ClaimsPrincipal user, CancellationToken ct) =>
                MapCheckIn(await svc.BoardAsync(user.GetCompanyId(), body.FlightId, body.PassengerId, ct))).RequireAuthorization(Manage);

            group.MapPost("/flights/{flightId:guid}/board-all", async (
                Guid flightId, [FromServices] CheckInService svc, ClaimsPrincipal user, CancellationToken ct) =>
                Results.Ok(new { boarded = await svc.BoardAllAsync(user.GetCompanyId(), flightId, ct) })).RequireAuthorization(Manage);

            return group;
        }

        private static IResult MapFlight(FlightOpResult r) => r.Code switch
        {
            "ok" => Results.Ok(r.Flight),
            "notFound" => Results.NotFound(),
            _ => Results.Conflict(new { error = r.Message, availableActions = r.AvailableActions }),
        };

        private static IResult MapCheckIn(CheckInResult r) => r.Code switch
        {
            "ok" => Results.Ok(r.CheckIn),
            "notFound" => Results.NotFound(new { error = r.Message }),
            _ => Results.Conflict(new { error = r.Message }),
        };
    }
}
