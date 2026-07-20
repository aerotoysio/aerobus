namespace AeroBus.Core.Data
{
    /// <summary>
    /// Every DocumentForge collection name, in one place, namespaced by module
    /// (<c>&lt;module&gt;.&lt;collection&gt;</c> — the module matches the endpoint
    /// group / permission resource). Repositories, services, raw-SQL sites and
    /// event subjects all reference these constants; never write a collection
    /// name as an inline string.
    ///
    /// Named <c>DfCollections</c> (not <c>Collections</c>) to avoid clashing with
    /// the <c>System.Collections</c> namespace.
    ///
    /// Two families deliberately live elsewhere:
    /// <list type="bullet">
    ///   <item><c>policystudio.*</c> — already namespaced at its store boundary
    ///   (<see cref="Repositories.PolicyStudio.PolicyStudioStore"/>).</item>
    ///   <item>The RuleForge contract collections (<c>rules</c>, <c>ruleversions</c>,
    ///   <c>environments</c>, <c>referencesets</c>, <c>referencesetversions</c>) —
    ///   read by the RuleForge engine from its own database; their names are its
    ///   contract, defined in <see cref="Services.Rules.RuleAuthoringService"/> and
    ///   NOT namespaced. (The unrelated <see cref="Admin.Environments"/> below is the
    ///   aerobus control-plane collection that previously shared that name.)</item>
    /// </list>
    /// </summary>
    public static class DfCollections
    {
        public static class Catalogue
        {
            public const string Continents = "catalogue.continents";
            public const string Countries = "catalogue.countries";
            public const string Regions = "catalogue.regions";
            public const string Airports = "catalogue.airports";
            public const string MarketZones = "catalogue.marketzones";
            public const string Equipment = "catalogue.equipment";
            public const string Layouts = "catalogue.layouts";
            public const string Schedules = "catalogue.schedules";
            public const string Flights = "catalogue.flights";
            public const string ConnectionRules = "catalogue.connectionrules";
            public const string Products = "catalogue.products";
            public const string Bundles = "catalogue.bundles";
            public const string Attributes = "catalogue.attributes";
            public const string Media = "catalogue.media";
            public const string StockKeepers = "catalogue.stockkeeper";
        }

        public static class Admin
        {
            public const string Companies = "admin.companies";
            public const string CompanyConfigs = "admin.companyconfigs";
            public const string PlatformConfig = "admin.platformconfig";
            public const string Workspaces = "admin.workspaces";
            public const string ApiTokens = "admin.apitokens";
            public const string Organisations = "admin.organisations";
            public const string Environments = "admin.environments";
        }

        public static class Identity
        {
            public const string OrgRoles = "identity.orgroles";
            public const string OrgRoleAssignments = "identity.orgroleassignments";
            public const string UserProfiles = "identity.userprofiles";
        }

        public static class Stock
        {
            public const string FlightInventory = "stock.flightinventory";
            public const string ProductCounters = "stock.productcounters";
        }

        public static class Customer
        {
            public const string Customers = "customers.customers";
        }

        public static class Offer
        {
            public const string Offers = "offers.offers";
        }

        public static class Order
        {
            public const string Orders = "orders.orders";
        }

        public static class Operations
        {
            public const string CheckIns = "operations.checkins";
        }

        public static class Events
        {
            public const string Outbox = "events.outboxevents";
            public const string Cursors = "events.eventcursors";
            public const string WebhookSubscriptions = "events.webhooksubscriptions";
        }
    }
}
