using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AeroBus.Api.Endpoints.Admin;
using AeroBus.Api.Endpoints.Catalogue;
using AeroBus.Api.Endpoints.Customer;
using AeroBus.Api.Endpoints.Diagnostics;
using AeroBus.Api.Endpoints.Offer;
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

            // Who-am-I probe: echoes the authenticated principal's claims.
            app.MapGet("/secure/me", (ClaimsPrincipal user) =>
            {
                var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? "unknown";
                var email = user.FindFirstValue(JwtRegisteredClaimNames.Email);
                var roles = user.FindAll(ClaimTypes.Role).Select(r => r.Value);
                var claims = user.FindAll("perm").Select(r => r.Value);
                return new { name, email, roles, claims };
            })
            .RequireAuthorization("users:view");

            // Admin (control plane) — route shapes identical to the ooms
            // admin-service so the existing admin UI can repoint later.
            app.MapGroup("/admin/companies").WithTags("Admin").AdminCompaniesMapping();
            app.MapGroup("/admin/companies/config").WithTags("Admin").AdminCompanyConfigMapping();
            app.MapGroup("/admin/roles").WithTags("Admin").AdminRolesMapping();
            app.MapGroup("/admin/roles/permissions").WithTags("Admin").AdminRolePermissionsMapping();
            app.MapGroup("/admin/permissions").WithTags("Admin").AdminPermissionsMapping();
            app.MapGroup("/admin/users").WithTags("Admin").AdminUsersMapping();
            app.MapGroup("/admin/workspaces").WithTags("Admin").AdminWorkspacesMapping();
            app.MapGroup("/admin/api-tokens").WithTags("Admin").AdminApiTokensMapping();

            // Catalogue — route shapes identical to the ooms admin-service.
            // Group-level RequireAuthorization is a deliberate deviation: ooms
            // left these anonymous (clearly accidental — every handler reads
            // the companyId claim), so a signed-in principal is required here.
            app.MapGroup("/catalogue/continents").WithTags("Catalogue").CatalogueContinentsMapping().RequireAuthorization();
            app.MapGroup("/catalogue/countries").WithTags("Catalogue").CatalogueCountriesMapping().RequireAuthorization();
            app.MapGroup("/catalogue/regions").WithTags("Catalogue").CatalogueRegionsMapping().RequireAuthorization();
            app.MapGroup("/catalogue/airports").WithTags("Catalogue").CatalogueAirportsMapping().RequireAuthorization();

            app.MapGroup("/catalogue/market-zones").WithTags("Catalogue").CatalogueMarketZonesMapping().RequireAuthorization();
            // market-zone-selectors are embedded in the market zone document (no standalone endpoint).

            app.MapGroup("/catalogue/equipment").WithTags("Catalogue").CatalogueEquipmentMapping().RequireAuthorization();
            // layout-compartments + seats + seat-types are embedded in the Layout aggregate document.
            app.MapGroup("/catalogue/layouts").WithTags("Catalogue").CatalogueLayoutsMapping().RequireAuthorization();
            app.MapGroup("/catalogue/schedules").WithTags("Catalogue").CatalogueSchedulesMapping().RequireAuthorization();
            app.MapGroup("/catalogue/flights").WithTags("Catalogue").CatalogueFlightsMapping().RequireAuthorization();
            app.MapGroup("/catalogue/connection-rules").WithTags("Catalogue").CatalogueConnectionRulesMapping().RequireAuthorization();
            app.MapGroup("/catalogue/flight-builder").WithTags("Catalogue").CatalogueFlightBuilderMapping().RequireAuthorization();
            app.MapGroup("/catalogue/bundles").WithTags("Catalogue").CatalogueBundlesMapping().RequireAuthorization();

            app.MapGroup("/catalogue/products").WithTags("Catalogue").CatalogueProductsMapping().RequireAuthorization();
            // Product metadata is embedded in the Product document (no standalone endpoint).

            app.MapGroup("/catalogue/stockkeeper").WithTags("Catalogue").CatalogueStockKeeperMapping().RequireAuthorization();

            // Customer
            app.MapGroup("/customer").WithTags("Customer Management").CustomersMapping().RequireAuthorization();

            // Offer distribution — shop + re-price (RuleForge decision points).
            // /offer/offer-engine/{slug} is intentionally NOT mapped (ooms node
            // handlers are permanently dropped).
            app.MapGroup("/offer").WithTags("Offer").OfferMapping().RequireAuthorization();

            // Rules authoring proxy over RuleForge's DocumentForge collections.
            app.MapGroup("/rules").WithTags("Rules").RulesMapping().RequireAuthorization();

            return app;
        }
    }
}
