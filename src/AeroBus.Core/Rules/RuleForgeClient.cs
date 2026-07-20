using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
        private readonly IRuleForgeSettingsProvider _settings;

        // Settings (base URL / key / timeout) resolve per CALL from platform
        // config (database-held, admin-editable at runtime) with the appsettings
        // bootstrap as fallback — so a settings change applies without a restart.
        public RuleForgeClient(HttpClient http, IRuleForgeSettingsProvider settings)
        {
            _http = http;
            _settings = settings;
            // The outer HttpClient timeout stays generous; the effective per-call
            // timeout is enforced via a linked token from the resolved settings.
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        private async Task<(RuleForgeSettings Settings, Uri Uri)> ResolveAsync(string endpoint, CancellationToken ct)
        {
            var s = await _settings.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(s.BaseUrl))
                throw new InvalidOperationException(
                    "RuleForge base URL is not configured (platform config 'ruleforge.baseUrl' or the RuleForge:BaseUrl bootstrap).");
            return (s, new Uri(s.BaseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/')));
        }

        private static HttpRequestMessage Request(HttpMethod method, Uri uri, RuleForgeSettings s, HttpContent? content = null)
        {
            var req = new HttpRequestMessage(method, uri) { Content = content };
            if (!string.IsNullOrWhiteSpace(s.ApiKey))
                req.Headers.TryAddWithoutValidation(ApiKeyHeader, s.ApiKey);
            return req;
        }

        private static CancellationTokenSource TimeoutScope(RuleForgeSettings s, CancellationToken ct)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(s.TimeoutMs));
            return cts;
        }

        public async Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default)
        {
            if (debug) endpoint += (endpoint.Contains('?') ? "&" : "?") + "debug=true";
            var (settings, uri) = await ResolveAsync(endpoint, ct);

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var timeout = TimeoutScope(settings, ct);
            using var request = Request(HttpMethod.Post, uri, settings, content);
            using var resp = await _http.SendAsync(request, timeout.Token);
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
                var (settings, uri) = await ResolveAsync("health", ct);
                using var timeout = TimeoutScope(settings, ct);
                using var request = Request(HttpMethod.Get, uri, settings);
                using var resp = await _http.SendAsync(request, timeout.Token);
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
                var (settings, uri) = await ResolveAsync("admin/refresh", ct);
                using var timeout = TimeoutScope(settings, ct);
                using var request = Request(HttpMethod.Post, uri, settings);
                using var resp = await _http.SendAsync(request, timeout.Token);
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
