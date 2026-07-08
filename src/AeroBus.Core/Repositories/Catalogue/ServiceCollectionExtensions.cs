using AeroBus.Core.Repositories.Shopping;
using AeroBus.Core.Repositories.Stock;
using AeroBus.Core.Services.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Catalogue
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the catalogue module: reference data (continents → airports),
        /// fleet (equipment/layouts), network (schedules/flights + flight builder),
        /// merchandising (products/bundles/stock keepers) and the shopping engines.
        /// Stock (flight inventory, product counters) and Shopping registrations
        /// are folded in here — in ooms they had no module of their own either.
        /// </summary>
        public static IServiceCollection AddCatalogue(this IServiceCollection services)
        {
            services.AddScoped<IAirports, Airports>();
            services.AddScoped<AirportService>();

            services.AddScoped<IAttributes, AttributesRepo>();
            services.AddScoped<AttributesService>();

            services.AddScoped<IBundles, Bundles>();
            services.AddScoped<BundleService>();

            services.AddScoped<IConnectionRules, ConnectionRules>();
            services.AddScoped<ConnectionRulesService>();

            services.AddScoped<IContinents, Continents>();
            services.AddScoped<ContinentsService>();

            services.AddScoped<ICountries, Countries>();
            services.AddScoped<CountriesService>();

            services.AddScoped<IEquipment, EquipmentRepo>();
            services.AddScoped<EquipmentService>();

            services.AddScoped<IFlightBuilder, FlightBuilder>();
            services.AddScoped<FlightBuilderService>();

            services.AddScoped<IFlights, Flights>();
            services.AddScoped<FlightsService>();

            services.AddScoped<IFlightSolutions, FlightSolutions>();
            services.AddScoped<FlightSolutionsService>();

            // Layout is one aggregate document (compartments/seats/seat-types embedded).
            services.AddScoped<ILayouts, Layouts>();
            services.AddScoped<LayoutsService>();

            services.AddScoped<IMarketZones, MarketZones>();
            services.AddScoped<MarketZoneService>();

            services.AddScoped<IProducts, Products>();
            services.AddScoped<ProductsService>();

            services.AddScoped<IRegions, Regions>();
            services.AddScoped<RegionsService>();

            services.AddScoped<ISchedules, Schedules>();
            services.AddScoped<SchedulesService>();

            // Registered here even though ooms had this pair commented out —
            // the /catalogue/stockkeeper endpoints were mapped there regardless,
            // which could not resolve at runtime. Fixed on the way through.
            services.AddScoped<IStockKeepers, StockKeepers>();
            services.AddScoped<StockKeeperService>();

            // Stock: per-flight inventory documents created by the flight builder,
            // and SKU/bucket product counters.
            services.AddScoped<IFlightInventories, FlightInventories>();
            services.AddScoped<IProductCounters, ProductCounters>();

            // Shopping projection over the flight-solutions engine (registered in
            // the ooms offer-management service; folded in here since AeroBus is
            // a single service).
            services.AddScoped<IDirectFlightSolutions, DirectFlightSolutions>();

            // Media repo/service exist but stay unregistered, mirroring ooms
            // (its registration was commented out and no endpoint was mapped).

            return services;
        }
    }
}
