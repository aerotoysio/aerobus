using AeroBus.Core.Repositories.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Services.Operations
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the Operations (departure control / DCS) module: the flight-keyed
        /// check-in repository, the manifest builder (fed from order-create), and the
        /// flight-operations + check-in services. Depends on the catalogue (flights),
        /// stock (inventory counters) and orders (<c>AddOrders</c> — order roll-up)
        /// being registered.
        /// </summary>
        public static IServiceCollection AddOperations(this IServiceCollection services)
        {
            services.AddScoped<ICheckIns, CheckIns>();
            services.AddScoped<IManifestBuilder, ManifestBuilder>();
            services.AddScoped<FlightOperationsService>();
            services.AddScoped<CheckInService>();
            return services;
        }
    }
}
