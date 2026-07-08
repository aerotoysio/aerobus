using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Common.Cache
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// In-process hot cache + the lazy resolvers over it (airport, time zone,
        /// MCT, company). No boot-time preloader and no configured CompanyId:
        /// resolvers populate the cache on first request. Redis is gone.
        /// </summary>
        public static IServiceCollection AddCache(this IServiceCollection services)
        {
            services.AddSingleton<IHotCache, HotCache>();

            // Resolvers are scoped because they read through IDocumentStore
            // (scoped); the singleton HotCache carries results across requests.
            services.AddScoped<IAirportResolver, CachedAirportResolver>();
            services.AddScoped<ITimeZoneResolver, CachedTimeZoneResolver>();
            services.AddScoped<IMctResolver, CachedMctResolver>();
            services.AddScoped<ICompanyResolver, CachedCompanyResolver>();

            return services;
        }
    }
}
