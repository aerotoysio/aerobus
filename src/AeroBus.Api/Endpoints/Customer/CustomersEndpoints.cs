using System.Security.Claims;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Customer;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Customer
{
    public static class CustomersEndpoints
    {
        public static RouteGroupBuilder CustomersMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", async (
                Guid id,
                [FromServices] CustomersService svc,
                ClaimsPrincipal user) =>
            {
                var item = await svc.GetByIdAsync(id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

            group.MapGet("/number/{customerNumber}", async (
                string customerNumber,
                [FromServices] CustomersService svc,
                ClaimsPrincipal user) =>
            {
                var item = await svc.GetByNumberAsync(customerNumber);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

            // Main LIST – paged + filters + search
            group.MapGet("/", async (
                [FromServices] CustomersService svc,
                ClaimsPrincipal user,
                [FromQuery] string? loyaltyProgram,
                [FromQuery] string? status,
                [FromQuery] string? search,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var companyId = user.GetCompanyId();
                var page = pageNumber.GetValueOrDefault(1);
                var size = pageSize.GetValueOrDefault(50);

                var items = await svc.ListByCompanyAsync(
                    companyId,
                    loyaltyProgram,
                    status,
                    search,
                    page,
                    size);

                return Results.Ok(items);
            });

            // Non-paged for cache
            group.MapGet("/all", async (
                [FromServices] CustomersService svc,
                ClaimsPrincipal user) =>
            {
                var companyId = user.GetCompanyId();
                var items = await svc.GetByCompanyAsync(companyId);
                return Results.Ok(items);
            });

            group.MapPost("/save", async (
                [FromBody] AeroBus.Core.Model.Customer.Customer customer,
                [FromServices] CustomersService svc,
                ClaimsPrincipal user) =>
            {
                try
                {
                    // Default the tenant from the caller's token so channel
                    // clients (e.g. AeroWeb check-in) don't have to know it.
                    if (customer.CompanyId is null || customer.CompanyId == Guid.Empty)
                        customer = customer with { CompanyId = user.GetCompanyId() };
                    var saved = await svc.SaveAsync(customer);
                    return Results.Ok(saved);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            });

            group.MapDelete("/{id:guid}", async (
                Guid id,
                [FromServices] CustomersService svc,
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
