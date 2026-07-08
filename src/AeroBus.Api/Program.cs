using AeroBus.Core.Data;

var builder = WebApplication.CreateBuilder(args);

// DocumentForge — the only required external dependency. Everything AeroBus
// persists goes through IDocumentStore, so a different datasource can be
// swapped in behind that seam without touching the domain.
builder.Services.AddDocumentForge(builder.Configuration);

var app = builder.Build();

AeroBus.Api.Bootstrap.AppEndpoints.Configure(app);

app.Run();
