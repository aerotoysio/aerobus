using AeroBus.Core.Data.Postgres;
using AeroBus.Core.Services.Customer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Customer
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the customer module: the customer aggregate (passports and
        /// stored cards embedded — one document per account holder). Customers
        /// are a Postgres system-of-record domain (docs/storage-architecture.md);
        /// the DocumentForge repository remains the fallback when Postgres is
        /// not configured.
        /// </summary>
        public static IServiceCollection AddCustomer(this IServiceCollection services, IConfiguration config)
        {
            if (config.PostgresEnabled())
                services.AddScoped<ICustomers, PgCustomers>();
            else
                services.AddScoped<ICustomers, Customers>();
            services.AddScoped<CustomersService>();

            // The single-identity pattern: order create links passengers to customers.
            services.AddScoped<Services.Customer.CustomerLinker>();

            return services;
        }
    }
}
