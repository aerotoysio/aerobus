using AeroBus.Api.Endpoints.Diagnostics;
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

            return app;
        }
    }
}
