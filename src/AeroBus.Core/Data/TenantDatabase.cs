using Microsoft.Extensions.Options;

namespace AeroBus.Core.Data
{
    /// <summary>
    /// Per-request holder for the DocumentForge database the caller's tenant reads
    /// and writes. The main (tenant-routed) <see cref="IDocumentForgeClient"/> reads
    /// <see cref="CurrentDatabase"/> on every call, so setting it once per request
    /// (in the tenant-routing middleware) points all of that request's business
    /// reads/writes at the org's own database.
    ///
    /// Defaults to the statically-configured <c>DocumentForge:Database</c> so
    /// unauthenticated paths, background services, dev and the existing single-DB
    /// tests keep working unchanged; the middleware overrides it to the tenant's
    /// short-name once an authenticated, provisioned org is on the request.
    /// </summary>
    public interface ITenantDatabase
    {
        string? CurrentDatabase { get; set; }
    }

    public sealed class TenantDatabase : ITenantDatabase
    {
        private readonly string? _default;
        private string? _current;

        public TenantDatabase(IOptions<DocumentForgeOptions> options) =>
            _default = string.IsNullOrWhiteSpace(options.Value.Database) ? null : options.Value.Database;

        public string? CurrentDatabase
        {
            get => _current ?? _default;
            set => _current = string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
