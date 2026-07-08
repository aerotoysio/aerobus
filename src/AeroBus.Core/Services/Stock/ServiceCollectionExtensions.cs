using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Services.Stock
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the seat-inventory sell/release service (DocumentForge
        /// conditional-update backed). Scoped like the other domain services; the
        /// (FlightId,Bucket)→_id cache inside is process-wide (static), so scope
        /// does not defeat it.
        /// </summary>
        public static IServiceCollection AddStock(this IServiceCollection services)
        {
            services.AddScoped<IInventoryService, InventoryService>();
            return services;
        }
    }
}
