using AeroBus.Core.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Services.Admin
{
    /// <summary>
    /// One-shot startup migration: move the RuleForge bootstrap values from
    /// appsettings into database-held platform config, once, if the keys aren't
    /// set yet — so an install configured the old way converges on
    /// database-held settings without hand-holding, and the settings become
    /// visible/editable in the admin surface. Never overwrites existing rows;
    /// best-effort (a DocumentForge that's still coming up must not crash
    /// AeroBus — registered after ControlDatabaseInitializer, so the control
    /// database exists by the time this runs).
    /// </summary>
    public sealed class PlatformConfigSeeder(
        IServiceScopeFactory scopes,
        IOptions<RuleForgeOptions> ruleForge,
        IOptions<Data.TenancyOptions> tenancy,
        ILogger<PlatformConfigSeeder> log) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<PlatformConfigService>();
                var b = ruleForge.Value;

                await config.SeedIfMissingAsync(
                    PlatformRuleForgeSettingsProvider.BaseUrlKey, b.BaseUrl, isSecret: false,
                    "RuleForge engine base URL.", cancellationToken);
                await config.SeedIfMissingAsync(
                    PlatformRuleForgeSettingsProvider.ApiKeyKey, b.ApiKey, isSecret: true,
                    "Shared secret sent to RuleForge as X-AERO-Key.", cancellationToken);
                await config.SeedIfMissingAsync(
                    PlatformRuleForgeSettingsProvider.TimeoutKey, b.TimeoutMs.ToString(), isSecret: false,
                    "Per-call RuleForge timeout in milliseconds.", cancellationToken);
                await config.SeedIfMissingAsync(
                    "tenancy.baseDomain", tenancy.Value.BaseDomain, isSecret: false,
                    "Base domain for subdomain tenancy (<shortName>.<baseDomain>); empty disables Host-based resolution.", cancellationToken);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Platform-config startup seed skipped (store unreachable?); settings fall back to appsettings until set.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
