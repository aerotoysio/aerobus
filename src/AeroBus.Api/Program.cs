using AeroBus.Api.Bootstrap;
using AeroBus.Core.Common.Cache;
using AeroBus.Core.Data;
using AeroBus.Core.Events;
using AeroBus.Core.Identity;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Repositories.Customer;
using AeroBus.Core.Rules;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Distribution;
using AeroBus.Core.Services.Rules;
using AeroBus.Core.Services.Stock;

var builder = WebApplication.CreateBuilder(args);

// DocumentForge — the only required external dependency. Everything AeroBus
// persists goes through IDocumentStore, so a different datasource can be
// swapped in behind that seam without touching the domain.
builder.Services.AddDocumentForge(builder.Configuration);

// Security: Keycloak-or-ApiKey authentication, permission-claim
// authorization, tenant context. Keycloak (the "Keycloak" section) is the
// identity source for interactive users; ab_ API keys cover programmatic
// agents. The legacy self-issued HS256 JWT path was removed.
builder.Services.AddSecurity(builder.Configuration);

// Identity: Keycloak-backed users/roles/organisations management behind
// /identity — the aerostudio admin UI path. Multi-tenant: one Keycloak
// organization per airline client.
builder.Services.AddIdentity(builder.Configuration);

// Admin (control plane): companies, users, roles, permissions, workspaces,
// company configs, API tokens.
builder.Services.AddAdmin();

// In-process hot cache + lazy resolvers (airport / time zone / MCT / company).
// No boot-time preload: resolvers populate the cache on first request.
builder.Services.AddCache();

// Catalogue: reference data, fleet, schedules/flights + flight builder (with
// per-flight inventory initialisation), products/bundles/stock, shopping engines.
builder.Services.AddCatalogue();

// Customer: the customer aggregate (passports/stored cards embedded).
builder.Services.AddCustomer();

// RuleForge: typed client + DecisionRunner for the named decision points.
// Bound from the "RuleForge" config section (BaseUrl/ApiKey/TimeoutMs/Endpoints).
builder.Services.AddRuleForge(builder.Configuration);

// Offer distribution: shop runtime, RuleForge bundle builder, re-pricing, offers repo.
builder.Services.AddOffer();

// Stock: seat-inventory sell/release over the DocumentForge conditional-update
// primitive (the order lifecycle decrements/releases through this).
builder.Services.AddStock();

// Order distribution: order aggregate repo + create/retrieve/change services
// (inventory decrement on create, release on cancel, RuleForge order decisions).
builder.Services.AddOrders();

// Rules authoring proxy over RuleForge's DocumentForge collections.
builder.Services.AddRulesAuthoring();

// Events backbone: transactional outbox publisher, the background dispatcher
// (webhook fan-out with retry/backoff), and webhook-subscription repo. The
// domain services depend on IEventPublisher to emit at their change sites.
builder.Services.AddEvents(builder.Configuration);

// OpenAPI/Swagger: one "v1" doc over every endpoint group, Bearer security so
// the "Authorize" button drives both JWT and ab_ keys. UI is gated below.
builder.Services.AddAeroBusSwagger();

var app = builder.Build();

// OpenAPI document + Swagger UI. The generated document is always available;
// the interactive UI is Development-only so we don't expose it in production.
// (A prod deployment that wants the UI can flip SWAGGER_UI=true.)
var enableSwaggerUi = app.Environment.IsDevelopment() ||
    string.Equals(builder.Configuration["SWAGGER_UI"], "true", StringComparison.OrdinalIgnoreCase);
if (enableSwaggerUi)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AeroBus API v1");
        options.DocumentTitle = "AeroBus API";
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Reads [CrossCompany] off the matched endpoint and toggles
// ITenantContext.BypassTenancy for the request, so tenant-aware reads may
// return cross-company data. Must run after auth so the principal is set.
app.UseTenantBypassForCrossCompany();

AeroBus.Api.Bootstrap.AppEndpoints.Configure(app);

app.Run();
