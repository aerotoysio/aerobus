using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Rules
{
    /// <summary>
    /// Typed <see cref="HttpClient"/> over the RuleForge HTTP service. Auth is the
    /// shared-secret <c>X-AERO-Key</c> header (RuleForge also accepts
    /// <c>Authorization: Bearer</c>, but the header keeps it distinct from
    /// AeroBus's own JWT/api-key auth). <c>/health</c> is open; everything else
    /// is gated when RuleForge has a key configured.
    /// </summary>
    public sealed class RuleForgeClient : IRuleForgeClient
    {
        public const string ApiKeyHeader = "X-AERO-Key";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _http;

        public RuleForgeClient(HttpClient http, IOptions<RuleForgeOptions> options)
        {
            _http = http;
            var opts = options.Value;

            if (string.IsNullOrWhiteSpace(opts.BaseUrl))
                throw new InvalidOperationException("RuleForge:BaseUrl is not configured.");

            _http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            _http.Timeout = TimeSpan.FromMilliseconds(opts.TimeoutMs <= 0 ? 2000 : opts.TimeoutMs);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                _http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, opts.ApiKey);
        }

        public async Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default)
        {
            // Endpoint is a bound rule route like "/v1/offer/shop-bundles" — join
            // it onto the base address, trimming the leading slash so the base
            // path isn't discarded.
            var relative = endpoint.TrimStart('/');
            if (debug) relative += (relative.Contains('?') ? "&" : "?") + "debug=true";

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(relative, content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"RuleForge {endpoint} returned {(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(body, 500)}",
                    inner: null,
                    statusCode: resp.StatusCode);

            var envelope = JsonSerializer.Deserialize<RuleForgeEnvelope>(body, JsonOptions);
            return envelope
                   ?? throw new InvalidOperationException($"RuleForge {endpoint} returned an unparseable envelope: {Truncate(body, 500)}");
        }

        public async Task<bool> HealthAsync(CancellationToken ct = default)
        {
            try
            {
                using var resp = await _http.GetAsync("health", ct);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            // POST /admin/refresh takes no body; auth flows via the default header.
            // Best-effort: a publish must not fail because RuleForge is momentarily
            // unreachable — the DF writes already landed; a later boot/refresh picks
            // up the new version. Return false so the caller can surface "not refreshed".
            try
            {
                using var resp = await _http.PostAsync("admin/refresh", content: null, ct);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    }
}
