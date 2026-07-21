using System.Security.Claims;
using AeroBus.Core.Model.Distribution;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Distribution;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Order
{
    /// <summary>
    /// Order lifecycle endpoints — routes identical to the ooms order-management
    /// service (<c>/order/create</c>, <c>/order/retrieve</c>, <c>/order/change</c>)
    /// so an existing client can repoint. Create decrements seat inventory and only
    /// confirms if every leg is secured (else a 409 with the reason); change/cancel
    /// releases inventory. Group-level <c>RequireAuthorization()</c> is applied in
    /// AppEndpoints, like the other domain groups.
    /// </summary>
    public static class OrderEndpoints
    {
        public static RouteGroupBuilder OrderMapping(this RouteGroupBuilder group)
        {
            // List: the caller's company's orders, newest first, paged; search
            // matches the public order id (VF...). Powers the aerodesk Orders
            // board and the aerostudio orders section.
            group.MapGet("/", async (
                [FromServices] AeroBus.Core.Repositories.Order.IOrders orders,
                ClaimsPrincipal user,
                [FromQuery] string? status,
                [FromQuery] string? search,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var page = Math.Max(1, pageNumber.GetValueOrDefault(1));
                var size = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 200);
                return Results.Ok(await orders.ListByCompanyAsync(companyId, status, search, page, size, ct));
            });

            // Create: bind an order against a shopped offer + bundle, secure seats
            // atomically, and confirm. 200 with the order view on success; 409 with
            // {reason} when inventory can't be secured or policy denies; 404 when the
            // offer/company isn't found.
            group.MapPost("/create", async (
                [FromBody] OrderCreateRequest request,
                [FromServices] OrderCreateService svc,
                ClaimsPrincipal user,
                HttpRequest http,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var debug = http.Query.ContainsKey("debug");
                var result = await svc.Create(request, companyId, debug, ct);

                if (result.Success)
                    return Results.Ok(result.Order);

                return result.Reason switch
                {
                    "companyNotFound" or "offerNotFound" => Results.NotFound(new { result.Reason, error = result.Message }),
                    _ => Results.Json(new { result.Reason, error = result.Message }, statusCode: StatusCodes.Status409Conflict),
                };
            });

            // Retrieve: by public OrderId (+ optional last name) or internal Guid.
            group.MapPost("/retrieve", async (
                [FromBody] OrderRetrieveRequest request,
                [FromServices] OrderRetrieveService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var response = await svc.Retrieve(request, companyId, ct);
                return Results.Ok(response);
            });

            // Change: drive the order state machine (Confirm/Cancel/Refund/…). A
            // Cancel releases the order's seat inventory back to the pool.
            group.MapPost("/change", async (
                [FromBody] OrderChangeRequest request,
                [FromServices] OrderChangeService svc,
                ClaimsPrincipal user,
                HttpRequest http,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var debug = http.Query.ContainsKey("debug");
                var response = await svc.ChangeStatus(request, companyId, debug, ct);
                return response.Success
                    ? Results.Ok(response)
                    : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
            });

            return group;
        }
    }
}
