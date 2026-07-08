using System.Net;
using System.Text.Encodings.Web;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Security
{
    /// <summary>
    /// Options for <see cref="ApiKeyAuthenticationHandler"/>. Defaults are
    /// fine for production — tweak only for tests.
    /// </summary>
    public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>The Authorization header type we accept; standard is Bearer.</summary>
        public string HeaderScheme { get; set; } = "Bearer";
    }

    /// <summary>
    /// Authenticates requests carrying <c>Authorization: Bearer ab_&lt;prefix&gt;_&lt;secret&gt;</c>.
    /// Successful auth populates the request's <c>User</c> with claims that
    /// mirror the user-JWT shape (companyId, perm) so authorisation policies
    /// apply uniformly regardless of credential type.
    /// </summary>
    public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        public const string SchemeName = "ApiKey";

        private readonly ApiTokenService _tokens;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ApiTokenService tokens)
            : base(options, logger, encoder)
        {
            _tokens = tokens;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Defer to the next scheme if no Authorization header is present —
            // we don't want to short-circuit JWT-bearer flows for unauthed requests.
            if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
                return AuthenticateResult.NoResult();

            var raw = headerValues.ToString();
            if (string.IsNullOrEmpty(raw)) return AuthenticateResult.NoResult();

            var schemePrefix = $"{Options.HeaderScheme} ";
            if (!raw.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.NoResult();

            var bearer = raw[schemePrefix.Length..].Trim();
            if (!ApiTokenService.LooksLikeApiKey(bearer))
            {
                // Not for us — let the JWT handler take it.
                return AuthenticateResult.NoResult();
            }

            var remoteIp = Context.Connection.RemoteIpAddress?.ToString();
            var record = await _tokens.ValidateAsync(bearer, remoteIp);
            if (record is null)
            {
                Logger.LogDebug("API-key auth rejected: bad/expired/revoked key from {Ip}", remoteIp);
                return AuthenticateResult.Fail("Invalid API key.");
            }

            var principal = ApiTokenService.BuildPrincipal(record, Scheme.Name);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // Standard 401 with WWW-Authenticate so callers know they need to retry with a key.
            Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            Response.Headers["WWW-Authenticate"] = $"{Options.HeaderScheme} realm=\"aerobus\", error=\"invalid_token\"";
            return Task.CompletedTask;
        }
    }
}
