namespace AeroBus.Core.Data
{
    /// <summary>
    /// Bootstrap fallback for subdomain tenancy (the runtime value is the
    /// platform config key <c>tenancy.baseDomain</c>). When a base domain is
    /// set (e.g. <c>aerotoys.io</c>), a request whose Host is
    /// <c>&lt;slug&gt;.&lt;baseDomain&gt;</c> resolves the org whose short name
    /// is that slug — the org's slug, subdomain and database name are the same
    /// word. Empty disables Host-based resolution entirely.
    /// </summary>
    public sealed class TenancyOptions
    {
        public const string SectionName = "Tenancy";

        public string? BaseDomain { get; set; }
    }
}
