using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AeroBus.Core.Common
{
    /// <summary>
    /// <c>GET /version</c> — public, unauthenticated, returns
    /// <c>{ sha, builtAt, image }</c> sourced from the <c>BUILD_SHA</c>,
    /// <c>BUILD_TIME</c>, and <c>BUILD_IMAGE</c> environment variables that
    /// CI bakes into the container image at build time. Designed so
    /// "is the deploy live?" answers with a single public curl, without
    /// needing an API token.
    /// </summary>
    public static class VersionEndpointExtensions
    {
        public static IEndpointRouteBuilder MapVersion(this IEndpointRouteBuilder app)
        {
            // Pre-resolve so we don't read environment per-request. CI sets
            // these once at image-build time; the values don't change while
            // the process is alive.
            var sha     = Environment.GetEnvironmentVariable("BUILD_SHA");
            var builtAt = Environment.GetEnvironmentVariable("BUILD_TIME");
            var image   = Environment.GetEnvironmentVariable("BUILD_IMAGE");

            var payload = new
            {
                sha     = string.IsNullOrWhiteSpace(sha)     ? "unknown" : sha,
                builtAt = string.IsNullOrWhiteSpace(builtAt) ? null      : builtAt,
                image   = string.IsNullOrWhiteSpace(image)   ? null      : image,
            };

            app.MapGet("/version", () => Results.Ok(payload))
               .WithTags("Diagnostics")
               .WithSummary("Build provenance for the running image (sha / builtAt / image). Public; no auth required.")
               .AllowAnonymous();

            return app;
        }
    }
}
