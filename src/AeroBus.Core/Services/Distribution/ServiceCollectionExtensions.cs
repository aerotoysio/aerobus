using AeroBus.Core.Repositories.Distribution;
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
    }
}
