namespace AeroBus.Core.Security
{
    /// <summary>
    /// The permission contract: every assignable permission code, grouped by
    /// resource. Endpoints enforce these via RequireAuthorization("&lt;code&gt;")
    /// (any policy name becomes a perm-claim requirement — see
    /// PermissionPolicyProvider); custom org roles may only reference codes
    /// listed here. The PermissionHandler wildcards still apply on top:
    /// admin.all passes everything, &lt;resource&gt;.all passes any action on the
    /// resource. Keep aerostudio's section manifest in step with this list.
    /// </summary>
    public static class PermissionCatalog
    {
        public sealed record PermissionDef(string Code, string Resource, string Description);

        private static PermissionDef[] Pair(string resource, string label) =>
        [
            new($"{resource}.view", resource, $"View {label}"),
            new($"{resource}.manage", resource, $"Create and change {label}"),
        ];

        public static readonly IReadOnlyList<PermissionDef> All =
        [
            new("dashboard.view", "dashboard", "See the dashboard"),
            .. Pair("org", "organisation profile and settings"),
            .. Pair("identity", "users"),
            .. Pair("role", "roles and their permissions"),
            .. Pair("agent", "programmatic (API-key) accounts"),
            .. Pair("offers", "offers"),
            .. Pair("ibe", "IBE content"),
            .. Pair("ancillary", "ancillary rules"),
            .. Pair("orders", "orders"),
            .. Pair("customers", "customers"),
            .. Pair("catalogue", "catalogue data (flights, products, bundles)"),
            .. Pair("rules", "business rules"),
            .. Pair("events", "events and webhook subscriptions"),
        ];

        private static readonly HashSet<string> Codes =
            All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static bool Exists(string code) => Codes.Contains(code);
    }
}
