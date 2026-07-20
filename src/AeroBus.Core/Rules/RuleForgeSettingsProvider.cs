using AeroBus.Core.Services.Admin;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Rules
{
    /// <summary>Effective RuleForge connection settings, resolved per call.</summary>
    public sealed record RuleForgeSettings(string BaseUrl, string ApiKey, int TimeoutMs);

    /// <summary>
    /// Where the RuleForge client gets its connection settings. Settings live in
    /// the DATABASE (platform config keys <c>ruleforge.baseUrl</c> /
    /// <c>ruleforge.apiKey</c> / <c>ruleforge.timeoutMs</c>, admin-editable at
    /// runtime); the appsettings <c>RuleForge</c> section remains only as the
    /// bootstrap fallback for keys the database doesn't hold — which also keeps
    /// dev and tests working with zero platform config.
    /// </summary>
    public interface IRuleForgeSettingsProvider
    {
        Task<RuleForgeSettings> GetAsync(CancellationToken ct = default);
    }

    public sealed class PlatformRuleForgeSettingsProvider(
        PlatformConfigService config,
        IOptions<RuleForgeOptions> bootstrap) : IRuleForgeSettingsProvider
    {
        public const string BaseUrlKey = "ruleforge.baseUrl";
        public const string ApiKeyKey = "ruleforge.apiKey";
        public const string TimeoutKey = "ruleforge.timeoutMs";

        public async Task<RuleForgeSettings> GetAsync(CancellationToken ct = default)
        {
            var b = bootstrap.Value;
            var baseUrl = await config.GetOrDefaultAsync(BaseUrlKey, b.BaseUrl, ct);
            var apiKey = await config.GetOrDefaultAsync(ApiKeyKey, b.ApiKey, ct);
            var timeoutMs = int.TryParse(await config.GetAsync(TimeoutKey, ct), out var t) && t > 0
                ? t
                : b.TimeoutMs;
            return new RuleForgeSettings(baseUrl, apiKey, timeoutMs <= 0 ? 2000 : timeoutMs);
        }
    }

    /// <summary>Fixed settings — what tests (and anything without a control database) use.</summary>
    public sealed class StaticRuleForgeSettingsProvider(RuleForgeSettings settings) : IRuleForgeSettingsProvider
    {
        public Task<RuleForgeSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);
    }
}
