using AeroBus.Core.Common.Cache;
using AeroBus.Core.Data;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Repositories.Customer;
using AeroBus.Core.Security;

var builder = WebApplication.CreateBuilder(args);

// DocumentForge — the only required external dependency. Everything AeroBus
// persists goes through IDocumentStore, so a different datasource can be
// swapped in behind that seam without touching the domain.
builder.Services.AddDocumentForge(builder.Configuration);

// Security: JWT-or-ApiKey authentication, permission-claim authorization,
// tenant context. Jwt settings come from the "Jwt" section (JWT_KEY env
// fallback for the signing key).
builder.Services.AddSecurity(builder.Configuration);

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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Reads [CrossCompany] off the matched endpoint and toggles
// ITenantContext.BypassTenancy for the request, so tenant-aware reads may
// return cross-company data. Must run after auth so the principal is set.
app.UseTenantBypassForCrossCompany();

AeroBus.Api.Bootstrap.AppEndpoints.Configure(app);

app.Run();
