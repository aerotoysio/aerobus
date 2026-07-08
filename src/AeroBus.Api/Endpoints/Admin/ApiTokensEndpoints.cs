using System.Security.Claims;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Admin
{
    /// <summary>
    /// CRUD-style endpoints for managing machine-to-machine API keys
    /// (<c>apitokens</c>). All operations are scoped to the caller's
    /// company — there is no cross-company API-key administration.
    /// </summary>
    public static class ApiTokensEndpoints
    {
        public static RouteGroupBuilder AdminApiTokensMapping(this RouteGroupBuilder group)
        {
            group.MapPost("/", async (
                    [FromBody] CreateApiTokenRequest request,
                    [FromServices] ApiTokenService svc,
                    ClaimsPrincipal user) =>
                {
                    var companyId = user.GetCompanyId();
                    if (companyId == Guid.Empty) return Results.BadRequest("No companyId in caller context.");

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest("Name is required.");

                    var created = await svc.GenerateAsync(
                        companyId,
                        request.Name,
                        request.Scopes,
                        request.Expires);

                    // The plaintext is included in the response exactly once. The
                    // record itself contains the hash, so callers must not echo or
                    // log the response body indiscriminately.
                    return Results.Ok(new CreateApiTokenResponse(
                        Id: created.Record.Id,
                        Prefix: created.Record.Prefix,
                        Plaintext: created.Plaintext,
                        Name: created.Record.Name,
                        Scopes: created.Record.Scopes,
                        Expires: created.Record.Expires,
                        Created: created.Record.Created));
                })
                .RequireAuthorization("apitoken.create")
                .WithSummary("Create a new API key for the caller's company. The plaintext is shown once and is not retrievable thereafter.");

            group.MapGet("/", async (
                    [FromServices] ApiTokenService svc,
                    ClaimsPrincipal user) =>
                {
                    var companyId = user.GetCompanyId();
                    if (companyId == Guid.Empty) return Results.BadRequest("No companyId in caller context.");

                    var rows = await svc.ListByCompanyAsync(companyId);
                    return Results.Ok(rows);
                })
                .RequireAuthorization("apitoken.view")
                .WithSummary("List API keys for the caller's company. Hashes and plaintexts are never returned.");

            group.MapPatch("/{id:guid}", async (
                    Guid id,
                    [FromBody] UpdateApiTokenRequest request,
                    [FromServices] ApiTokenService svc,
                    ClaimsPrincipal user) =>
                {
                    var companyId = user.GetCompanyId();
                    if (companyId == Guid.Empty) return Results.BadRequest("No companyId in caller context.");

                    // Defensively scope: list-and-verify rather than trusting
                    // the caller — explicit checks give better 404s than empty
                    // results.
                    var owned = await svc.ListByCompanyAsync(companyId);
                    if (!owned.Any(t => t.Id == id))
                        return Results.NotFound();

                    var updated = await svc.UpdateAsync(
                        id,
                        request.Name,
                        request.Scopes,
                        request.Expires,
                        request.ClearExpires);

                    return updated is null ? Results.NotFound() : Results.Ok(updated);
                })
                .RequireAuthorization("apitoken.update")
                .WithSummary("Update editable metadata (name, scopes, expires) on an API key. The secret cannot be changed — revoke and re-issue if needed.");

            group.MapDelete("/{id:guid}", async (
                    Guid id,
                    [FromServices] ApiTokenService svc,
                    ClaimsPrincipal user) =>
                {
                    var companyId = user.GetCompanyId();
                    if (companyId == Guid.Empty) return Results.BadRequest("No companyId in caller context.");

                    // Defensively scope: a caller can only revoke tokens belonging
                    // to their own company. List-and-check because Revoke is rare
                    // and the company already has a bounded set of tokens.
                    var owned = await svc.ListByCompanyAsync(companyId);
                    if (!owned.Any(t => t.Id == id))
                        return Results.NotFound();

                    await svc.RevokeAsync(id);
                    return Results.NoContent();
                })
                .RequireAuthorization("apitoken.revoke")
                .WithSummary("Revoke an API key. Idempotent. The row is retained for audit; subsequent uses return 401.");

            return group;
        }
    }

    /// <summary>Body for <c>POST /admin/api-tokens</c>.</summary>
    public sealed record CreateApiTokenRequest(
        string Name,
        string? Scopes = null,
        DateTime? Expires = null);

    /// <summary>Body for <c>PATCH /admin/api-tokens/{id}</c>. Any field left
    /// null is unchanged; <c>ClearExpires=true</c> explicitly nulls an
    /// existing expiry (since <c>Expires=null</c> means "no change").</summary>
    public sealed record UpdateApiTokenRequest(
        string? Name = null,
        string? Scopes = null,
        DateTime? Expires = null,
        bool ClearExpires = false);

    /// <summary>Response for <c>POST /admin/api-tokens</c> — the plaintext is
    /// the only opportunity to capture the key.</summary>
    public sealed record CreateApiTokenResponse(
        Guid Id,
        string Prefix,
        string Plaintext,
        string Name,
        string? Scopes,
        DateTime? Expires,
        DateTime? Created);
}
