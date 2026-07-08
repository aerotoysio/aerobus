using AeroBus.Core.Repositories.Distribution;
using AeroBus.Core.Repositories.Order;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Services.Distribution
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the offer distribution module: the offer-shop runtime, the
        /// RuleForge-backed bundle builder, the offer re-pricing service and the
        /// offers repository. Depends on the catalogue (bundles/products/flight
        /// solutions), the cache and RuleForge (<c>AddRuleForge</c>) being registered.
        /// </summary>
        public static IServiceCollection AddOffer(this IServiceCollection services)
        {
            services.AddScoped<IOffers, Offers>();
            services.AddScoped<IShopBundleBuilder, RuleForgeShopBundleBuilder>();
            services.AddScoped<OfferShopService>();
            services.AddScoped<OfferPriceService>();
            return services;
        }

        /// <summary>
        /// Registers the order distribution module: the order aggregate repository
        /// plus the create / retrieve / change services. Follows the
        /// <see cref="AddOffer"/> pattern (repo + services in one registration).
        /// Depends on Admin (companies), the offers repo (<c>AddOffer</c>), stock
        /// (<c>AddStock</c> — the inventory service) and RuleForge being registered.
        /// </summary>
        public static IServiceCollection AddOrders(this IServiceCollection services)
        {
            services.AddScoped<IOrders, Orders>();
            services.AddScoped<OrderCreateService>();
            services.AddScoped<OrderRetrieveService>();
            services.AddScoped<OrderChangeService>();
            return services;
        }
    }
}
