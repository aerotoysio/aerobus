using AeroBus.Core.Services.Customer;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Customer
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the customer module: the customer aggregate (passports and
        /// stored cards embedded — one document per account holder).
        /// </summary>
        public static IServiceCollection AddCustomer(this IServiceCollection services)
        {
            services.AddScoped<ICustomers, Customers>();
            services.AddScoped<CustomersService>();

            return services;
        }
    }
}
