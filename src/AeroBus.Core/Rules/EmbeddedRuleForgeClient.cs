using System.Collections.Concurrent;
using System.Text.Json;
using AeroBus.Core.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RuleForge.Core;
using RuleForge.Core.Graph;
using RuleForge.Core.Loader;
using RuleForge.Core.Models;
using RuleForge.DocumentForge;

namespace AeroBus.Core.Rules
{
    /// <summary>
    /// The RuleForge engine IN-PROCESS: same <see cref="RuleRunner"/>, same
    /// DocumentForge-backed rule/reference-set sources, no HTTP hop and no
    /// separate service to keep alive. Selected when the effective RuleForge
    /// base URL is blank or the literal <c>embedded</c> (see
    /// <see cref="RoutedRuleForgeClient"/>); publish refresh is a cache clear
    /// in the same process, so a published version is live immediately.
    /// Sources are cached per environment name (the env decides which
    /// <c>ruleBindings</c> pin versions).
    /// </summary>
    public sealed class EmbeddedRuleForgeClient : IRuleForgeClient, IAsyncDisposable
    {
        private sealed record EngineParts(DocumentForgeRuleSource Rules, DocumentForgeReferenceSetSource RefSets);

        private readonly IOptions<DocumentForgeOptions> _dfOptions;
        private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopes;
        private readonly ILogger<EmbeddedRuleForgeClient> _log;
        private readonly HttpClient _http = new(); // engine API-node calls + DfClient transport
        private readonly RuleRunner _runner = new();
        private readonly ConcurrentDictionary<string, EngineParts> _partsByEnv = new(StringComparer.OrdinalIgnoreCase);

        public EmbeddedRuleForgeClient(
            IOptions<DocumentForgeOptions> dfOptions,
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes,
            ILogger<EmbeddedRuleForgeClient> log)
        {
            _dfOptions = dfOptions;
            _scopes = scopes;
            _log = log;
        }

        // Singleton client, scoped settings: resolve the provider per call so
        // platform-config changes (env, engine selection) apply live.
        private async Task<RuleForgeSettings> SettingsAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var provider = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<IRuleForgeSettingsProvider>(scope.ServiceProvider);
            return await provider.GetAsync(ct).ConfigureAwait(false);
        }

        private EngineParts PartsFor(string env) =>
            _partsByEnv.GetOrAdd(env, e =>
            {
                var df = _dfOptions.Value;
                var client = new DfClient(_http, df.BaseUrl, df.ApiKey);
                return new EngineParts(
                    new DocumentForgeRuleSource(client, e),
                    new DocumentForgeReferenceSetSource(client));
            });

        public async Task<RuleForgeEnvelope> EvaluateAsync(
            string endpoint, object payload, bool debug = false, CancellationToken ct = default)
        {
            var settings = await SettingsAsync(ct).ConfigureAwait(false);
            var parts = PartsFor(settings.Env);

            // Strip any query string — debug travels as an Options flag in-proc.
            var path = endpoint.Split('?')[0];
            if (!path.StartsWith('/')) path = "/" + path;

            var rule = await parts.Rules.GetByEndpointAsync(path, HttpMethodKind.POST, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException($"No rule is bound to POST {path} in environment '{settings.Env}'.");

            var request = JsonSerializer.SerializeToElement(payload, AeroJson.Options);
            var envelope = await _runner.RunAsync(rule, request, new RuleRunner.Options(
                Debug: debug,
                SubRuleSource: parts.Rules,
                ReferenceSetSource: parts.RefSets,
                HttpClient: _http,
                RedactTraceErrors: true), ct).ConfigureAwait(false);

            // The engine envelope and the aerobus wire record are the same JSON
            // contract — bridge through the engine's own serializer options.
            var json = JsonSerializer.Serialize(envelope, AeroJson.Options);
            return JsonSerializer.Deserialize<RuleForgeEnvelope>(json, AeroJson.Options)
                   ?? throw new InvalidOperationException($"Embedded evaluation of {path} produced an unreadable envelope.");
        }

        public async Task<bool> HealthAsync(CancellationToken ct = default)
        {
            try
            {
                var settings = await SettingsAsync(ct).ConfigureAwait(false);
                await PartsFor(settings.Env).Rules.ListBindingsAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Embedded rule engine health probe failed (rule store unreachable?).");
                return false;
            }
        }

        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            // Publisher and evaluator share a process: a refresh is a cache
            // clear, so the new version serves on the very next evaluation.
            foreach (var parts in _partsByEnv.Values)
            {
                await parts.Rules.RefreshAsync(ct).ConfigureAwait(false);
                await parts.RefSets.RefreshAsync(ct).ConfigureAwait(false);
            }
            return true;
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Picks the engine per call from the database-held settings: a blank or
    /// <c>embedded</c> base URL runs in-process, a URL calls the standalone
    /// service — switchable at runtime from Platform Settings, no restart.
    /// </summary>
    public sealed class RoutedRuleForgeClient : IRuleForgeClient
    {
        private readonly IRuleForgeSettingsProvider _settings;
        private readonly EmbeddedRuleForgeClient _embedded;
        private readonly RuleForgeClient _remote;

        public RoutedRuleForgeClient(
            IRuleForgeSettingsProvider settings, EmbeddedRuleForgeClient embedded, RuleForgeClient remote)
        {
            _settings = settings;
            _embedded = embedded;
            _remote = remote;
        }

        private async Task<IRuleForgeClient> PickAsync(CancellationToken ct)
        {
            var s = await _settings.GetAsync(ct).ConfigureAwait(false);
            return IsEmbedded(s.BaseUrl) ? _embedded : _remote;
        }

        public static bool IsEmbedded(string? baseUrl) =>
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.Equals(baseUrl.Trim(), "embedded", StringComparison.OrdinalIgnoreCase);

        public async Task<RuleForgeEnvelope> EvaluateAsync(string endpoint, object payload, bool debug = false, CancellationToken ct = default) =>
            await (await PickAsync(ct).ConfigureAwait(false)).EvaluateAsync(endpoint, payload, debug, ct).ConfigureAwait(false);

        public async Task<bool> HealthAsync(CancellationToken ct = default) =>
            await (await PickAsync(ct).ConfigureAwait(false)).HealthAsync(ct).ConfigureAwait(false);

        public async Task<bool> RefreshAsync(CancellationToken ct = default) =>
            await (await PickAsync(ct).ConfigureAwait(false)).RefreshAsync(ct).ConfigureAwait(false);
    }
}
