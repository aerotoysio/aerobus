using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Services.Rules
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the rules authoring proxy. Depends on <c>AddDocumentForge</c>
        /// (raw string-id doc access) and <c>AddRuleForge</c> (refresh on publish).
        /// </summary>
        public static IServiceCollection AddRulesAuthoring(this IServiceCollection services)
        {
            services.AddScoped<RuleAuthoringService>();
            return services;
        }
    }
}
