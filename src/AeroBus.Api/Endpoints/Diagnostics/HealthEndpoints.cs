using AeroBus.Core.Data;

namespace AeroBus.Api.Endpoints.Diagnostics
{
    public static class HealthEndpoints
    {
        public static RouteGroupBuilder HealthMapping(this RouteGroupBuilder group)
        {
            group.MapGet("/", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow.ToString("u") }))
                .WithName("Liveness")
                .WithSummary("Liveness probe — returns 200 if the process is up.");

            group.MapGet("/documentforge", async (IDocumentForgeClient client, CancellationToken ct) =>
            {
                var result = await client.HealthAsync(ct);
                return result.Reachable
                    ? Results.Ok(new { status = "ok", documentforge = result })
                    : Results.Json(new { status = "down", documentforge = result }, statusCode: 503);
            })
            .WithName("DocumentForgeHealth")
            .WithSummary("Pings DocumentForge /health and returns reachability + response.");

            return group;
        }
    }
}
