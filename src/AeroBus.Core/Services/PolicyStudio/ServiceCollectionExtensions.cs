using AeroBus.Core.Repositories.PolicyStudio;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Services.PolicyStudio
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the Policy Studio module: the DocumentForge-backed authoring
        /// store and the service that owns the content lifecycle, evaluation
        /// (<see cref="TestRunner"/>) and compilation (<see cref="RuleCompiler"/>).
        /// Publishing reuses <c>AddRulesAuthoring</c>'s <see cref="Rules.RuleAuthoringService"/>,
        /// so that must be registered too (it is, in Program.cs).
        /// </summary>
        public static IServiceCollection AddPolicyStudio(this IServiceCollection services)
        {
            services.AddScoped<PolicyStudioStore>();
            services.AddScoped<PolicyStudioService>();

            return services;
        }
    }
}
