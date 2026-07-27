using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Rules
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the RuleForge integration: the typed <see cref="IRuleForgeClient"/>
        /// (bound to the "RuleForge" config section) and the <see cref="DecisionRunner"/>
        /// that applies per-decision-point failure modes. Optional per-point overrides
        /// are read from "RuleForge:FailureModes".
        /// </summary>
        public static IServiceCollection AddRuleForge(this IServiceCollection services, IConfiguration config)
        {
            var section = config.GetSection(RuleForgeOptions.SectionName);
            services.Configure<RuleForgeOptions>(section);
            services.Configure<DecisionRunnerOptions>(opts =>
            {
                var modes = section.GetSection("FailureModes").Get<Dictionary<string, FailureMode>>();
                if (modes is not null) opts.FailureModes = modes;
            });

            // Connection settings resolve per call from platform config
            // (database-held, admin-editable) with this section as bootstrap.
            services.AddScoped<IRuleForgeSettingsProvider, PlatformRuleForgeSettingsProvider>();

            // The engine: EMBEDDED in-process by default (base URL "embedded" or
            // blank), or the standalone RuleForge host when a URL is configured —
            // routed per call, switchable at runtime from Platform Settings.
            services.AddSingleton<EmbeddedRuleForgeClient>();
            services.AddHttpClient<RuleForgeClient>();
            services.AddScoped<IRuleForgeClient, RoutedRuleForgeClient>();
            services.AddScoped<DecisionRunner>();
            return services;
        }
    }
}
