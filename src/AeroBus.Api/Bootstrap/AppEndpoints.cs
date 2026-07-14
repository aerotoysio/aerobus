using AeroBus.Api.Endpoints.Admin;
using AeroBus.Api.Endpoints.Catalogue;
using AeroBus.Api.Endpoints.Customer;
using AeroBus.Api.Endpoints.Diagnostics;
using AeroBus.Api.Endpoints.Events;
using AeroBus.Api.Endpoints.Identity;
using AeroBus.Api.Endpoints.Offer;
using AeroBus.Api.Endpoints.Order;
using AeroBus.Api.Endpoints.Rules;
using AeroBus.Core.Common;

namespace AeroBus.Api.Bootstrap
{
    /// <summary>
    /// Single place where every endpoint group is attached to the app. Modules
    /// register here as they are ported: Admin, Catalogue, Customer, Offer,
    /// Order, Rules, Events.
    /// </summary>
    public static class AppEndpoints
    {
        public static WebApplication Configure(WebApplication app)
        {
            app.MapGroup("/health").WithTags("Health").HealthMapping();
            app.MapVersion();

            // Admin (control plane) — companies/config/workspaces only. User,
            // role, permission and api-token management all live under
            // /identity (Keycloak-backed); the ooms-era /admin/users (incl. the
            // HS256 authenticate endpoint), /admin/roles, /admin/permissions
            // and /admin/api-tokens surfaces were removed with the legacy user
            // stack. The who-am-I probe is /identity/me.
            app.MapGroup("/admin/companies").WithTags("Admin").AdminCompaniesMapping();
            app.MapGroup("/admin/companies/config").WithTags("Admin").AdminCompanyConfigMapping();
            app.MapGroup("/admin/workspaces").WithTags("Admin").AdminWorkspacesMapping();

            // Catalogue — route shapes identical to the ooms admin-service.
            // Group-level RequireAuthorization is a deliberate deviation: ooms
            // left these anonymous (clearly accidental — every handler reads
            // the companyId claim), so a signed-in principal is required here.
            app.MapGroup("/catalogue/continents").WithTags("Catalogue").CatalogueContinentsMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/countries").WithTags("Catalogue").CatalogueCountriesMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/regions").WithTags("Catalogue").CatalogueRegionsMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/airports").WithTags("Catalogue").CatalogueAirportsMapping().RequireAuthorization("catalogue.view");

            app.MapGroup("/catalogue/market-zones").WithTags("Catalogue").CatalogueMarketZonesMapping().RequireAuthorization("catalogue.view");
            // market-zone-selectors are embedded in the market zone document (no standalone endpoint).

            app.MapGroup("/catalogue/equipment").WithTags("Catalogue").CatalogueEquipmentMapping().RequireAuthorization("catalogue.view");
            // layout-compartments + seats + seat-types are embedded in the Layout aggregate document.
            app.MapGroup("/catalogue/layouts").WithTags("Catalogue").CatalogueLayoutsMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/schedules").WithTags("Catalogue").CatalogueSchedulesMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/flights").WithTags("Catalogue").CatalogueFlightsMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/connection-rules").WithTags("Catalogue").CatalogueConnectionRulesMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/flight-builder").WithTags("Catalogue").CatalogueFlightBuilderMapping().RequireAuthorization("catalogue.view");
            app.MapGroup("/catalogue/bundles").WithTags("Catalogue").CatalogueBundlesMapping().RequireAuthorization("catalogue.view");

            app.MapGroup("/catalogue/products").WithTags("Catalogue").CatalogueProductsMapping().RequireAuthorization("catalogue.view");
            // Product metadata is embedded in the Product document (no standalone endpoint).

            app.MapGroup("/catalogue/stockkeeper").WithTags("Catalogue").CatalogueStockKeeperMapping().RequireAuthorization("catalogue.view");

            // Identity — Keycloak-backed users/roles/organisations (the aerostudio
            // path). Org scoping lives in IdentityService; onboarding is the one
            // anonymous route (login-page tenant self-creation — gate before prod).
            app.MapGroup("/identity").WithTags("Identity").IdentityMapping().RequireAuthorization();
            app.MapPost("/identity/onboarding", IdentityEndpoints.OnboardAsync).WithTags("Identity").AllowAnonymous();

            // Customer. Domain groups carry a resource-level permission (see
            // PermissionCatalog): the policy name is a perm-claim requirement, and
            // admin.all / <resource>.all wildcards satisfy it, so legacy admin
            // principals and org-admins pass unchanged.
            app.MapGroup("/customer").WithTags("Customer Management").CustomersMapping().RequireAuthorization("customers.view");

            // Offer distribution — shop + re-price (RuleForge decision points).
            // /offer/offer-engine/{slug} is intentionally NOT mapped (ooms node
            // handlers are permanently dropped).
            app.MapGroup("/offer").WithTags("Offer").OfferMapping().RequireAuthorization("offers.view");

            // Order lifecycle — create (inventory decrement) / retrieve / change
            // (cancel releases inventory). Routes identical to the ooms
            // order-management service.
            app.MapGroup("/order").WithTags("Order").OrderMapping().RequireAuthorization("orders.view");

            // Rules authoring proxy over RuleForge's DocumentForge collections.
            app.MapGroup("/rules").WithTags("Rules").RulesMapping().RequireAuthorization("rules.view");

            // Events backbone — outbox audit trail (/events), SSE stream
            // (/events/stream), and webhook subscription CRUD
            // (/events/subscriptions). All company-scoped.
            app.MapGroup("/events").WithTags("Events").EventsMapping().RequireAuthorization("events.view");

            return app;
        }
    }
}
