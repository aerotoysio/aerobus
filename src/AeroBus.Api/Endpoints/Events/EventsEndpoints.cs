using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroBus.Core.Events;
using AeroBus.Core.Security;
using Microsoft.AspNetCore.Mvc;

namespace AeroBus.Api.Endpoints.Events
{
    /// <summary>
    /// The event backbone's public surface, all scoped to the caller's company:
    /// <list type="bullet">
    ///   <item><c>/events</c> — the outbox as an audit trail (filter by type/status/from).</item>
    ///   <item><c>/events/stream</c> — Server-Sent Events: replay from a Seq cursor, then live-tail.</item>
    ///   <item><c>/events/subscriptions</c> — webhook subscription CRUD.</item>
    /// </list>
    /// Group-level <c>RequireAuthorization()</c> is applied in AppEndpoints.
    /// </summary>
    public static class EventsEndpoints
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static RouteGroupBuilder EventsMapping(this RouteGroupBuilder group)
        {
            MapAudit(group);
            MapStream(group);
            MapSubscriptions(group);
            return group;
        }

        // ── GET /events — audit list ──────────────────────────────────────────
        private static void MapAudit(RouteGroupBuilder group)
        {
            group.MapGet("/", async (
                [FromQuery] string? type,
                [FromQuery] string? status,
                [FromQuery] long? from,
                [FromQuery] int? limit,
                [FromServices] IOutbox outbox,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var take = Clamp(limit ?? 100, 1, 500);
                var rows = await outbox.ListForCompanyAsync(companyId, type, status, from ?? 0, take, ct);
                return Results.Ok(rows.Select(EventEnvelope.From));
            });
        }

        // ── GET /events/stream — SSE (replay + live tail) ─────────────────────
        private static void MapStream(RouteGroupBuilder group)
        {
            group.MapGet("/stream", async (
                [FromQuery] string? types,
                [FromQuery] long? from,
                [FromServices] IOutbox outbox,
                ClaimsPrincipal user,
                HttpContext http,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var patterns = ParseTypes(types);

                http.Response.Headers.CacheControl = "no-cache";
                http.Response.Headers.ContentType = "text/event-stream";
                http.Response.Headers["X-Accel-Buffering"] = "no";

                var cursor = from ?? 0;
                var lastHeartbeat = DateTime.UtcNow;

                // Replay everything already in the outbox past the cursor, then poll
                // for new rows every ~1s, advancing the Seq cursor. Heartbeat comments
                // keep the connection (and any proxy) alive between events.
                while (!ct.IsCancellationRequested)
                {
                    IReadOnlyList<AeroBus.Core.Events.OutboxEvent> batch;
                    try
                    {
                        batch = await outbox.TailForCompanyAsync(companyId, cursor, 200, ct);
                    }
                    catch (OperationCanceledException) { break; }

                    var wroteAny = false;
                    foreach (var row in batch)
                    {
                        cursor = Math.Max(cursor, row.Seq);
                        if (patterns.Count > 0 && !EventTypeMatch.MatchesAny(patterns, row.Type))
                            continue;

                        var payload = JsonSerializer.Serialize(EventEnvelope.From(row), Json);
                        await WriteAsync(http, $"id: {row.Seq}\nevent: {row.Type}\ndata: {payload}\n\n", ct);
                        wroteAny = true;
                    }

                    if (wroteAny) lastHeartbeat = DateTime.UtcNow;
                    else if (DateTime.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(15))
                    {
                        await WriteAsync(http, ": heartbeat\n\n", ct);
                        lastHeartbeat = DateTime.UtcNow;
                    }

                    try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
                    catch (OperationCanceledException) { break; }
                }

                return Results.Empty;
            });
        }

        // ── /events/subscriptions — webhook CRUD ──────────────────────────────
        private static void MapSubscriptions(RouteGroupBuilder group)
        {
            var subs = group.MapGroup("/subscriptions");

            subs.MapGet("/", async (
                [FromServices] IWebhookSubscriptions repo, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var list = await repo.GetByCompanyAsync(companyId, ct);
                return Results.Ok(list.Select(Public));
            });

            subs.MapGet("/{id:guid}", async (
                Guid id, [FromServices] IWebhookSubscriptions repo, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var sub = await repo.GetByIdAsync(id, ct);
                return sub is null || sub.CompanyId != companyId
                    ? Results.NotFound()
                    : Results.Ok(Public(sub));
            });

            subs.MapPost("/", async (
                [FromBody] WebhookSubscriptionRequest req,
                [FromServices] IWebhookSubscriptions repo,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                if (string.IsNullOrWhiteSpace(req.Url) || !Uri.TryCreate(req.Url, UriKind.Absolute, out _))
                    return Results.BadRequest(new { error = "A valid absolute Url is required." });

                var sub = new WebhookSubscription
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Url = req.Url,
                    Types = NormalizeTypes(req.Types),
                    Secret = string.IsNullOrWhiteSpace(req.Secret) ? GenerateSecret() : req.Secret!,
                    Active = req.Active ?? true,
                };
                var saved = await repo.SaveAsync(sub, ct) ?? sub;
                // The create response includes the secret ONCE so the caller can store
                // it; the list/get views omit it.
                return Results.Created($"/events/subscriptions/{saved.Id}", WithSecret(saved));
            });

            subs.MapPut("/{id:guid}", async (
                Guid id,
                [FromBody] WebhookSubscriptionRequest req,
                [FromServices] IWebhookSubscriptions repo,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var existing = await repo.GetByIdAsync(id, ct);
                if (existing is null || existing.CompanyId != companyId)
                    return Results.NotFound();

                if (!string.IsNullOrWhiteSpace(req.Url) && !Uri.TryCreate(req.Url, UriKind.Absolute, out _))
                    return Results.BadRequest(new { error = "Url must be a valid absolute URI." });

                var updated = existing with
                {
                    Url = string.IsNullOrWhiteSpace(req.Url) ? existing.Url : req.Url!,
                    Types = req.Types is null ? existing.Types : NormalizeTypes(req.Types),
                    Secret = string.IsNullOrWhiteSpace(req.Secret) ? existing.Secret : req.Secret!,
                    Active = req.Active ?? existing.Active,
                };
                var saved = await repo.SaveAsync(updated, ct) ?? updated;
                return Results.Ok(Public(saved));
            });

            subs.MapDelete("/{id:guid}", async (
                Guid id, [FromServices] IWebhookSubscriptions repo, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var companyId = user.GetCompanyId();
                var existing = await repo.GetByIdAsync(id, ct);
                if (existing is null || existing.CompanyId != companyId)
                    return Results.NotFound();
                await repo.DeleteAsync(id, ct);
                return Results.NoContent();
            });
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>List/get view — omits the secret.</summary>
        private static object Public(WebhookSubscription s) =>
            new { s.Id, s.CompanyId, s.Url, s.Types, s.Active, s.Created, s.Updated };

        /// <summary>Create view — includes the secret once.</summary>
        private static object WithSecret(WebhookSubscription s) =>
            new { s.Id, s.CompanyId, s.Url, s.Types, s.Secret, s.Active, s.Created, s.Updated };

        private static string[] NormalizeTypes(string[]? types) =>
            types is null || types.Length == 0
                ? new[] { "*" }
                : types.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();

        private static List<string> ParseTypes(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static string GenerateSecret()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static async Task WriteAsync(HttpContext http, string frame, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(frame);
            await http.Response.Body.WriteAsync(bytes, ct);
            await http.Response.Body.FlushAsync(ct);
        }

        private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
    }

    /// <summary>Create/update payload for a webhook subscription.</summary>
    public sealed record WebhookSubscriptionRequest(
        string? Url,
        string[]? Types,
        string? Secret,
        bool? Active);
}
