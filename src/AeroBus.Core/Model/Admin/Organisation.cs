using AeroBus.Core.Model;

namespace AeroBus.Core.Model.Admin
{
    /// <summary>
    /// The SaaS control-plane record for a tenant airline — the routing source of
    /// truth that maps a Keycloak organisation to its own DocumentForge database.
    ///
    /// Lives in the <c>organisations</c> collection in the fixed CONTROL database
    /// (never a tenant DB — it's what tells the router which tenant DB to use, so it
    /// can't live inside one). <see cref="Id"/> equals the Keycloak organisation id,
    /// which is the <c>companyId</c> claim on every user token, so a request resolves
    /// its database as: <c>companyId → this row → ShortName</c>.
    /// </summary>
    public sealed record Organisation : IDocument
    {
        public Guid Id { get; init; }              // = Keycloak org id (= companyId claim)
        public string OrgAlias { get; init; } = string.Empty;   // Keycloak org alias (slug)
        public string ShortName { get; init; } = string.Empty;  // the DocumentForge database name, e.g. "ek"
        public string Name { get; init; } = string.Empty;

        // Denormalised airline identity (also written onto the tenant DB's Company doc).
        public string? Designator { get; init; }        // order-id prefix, e.g. "EK"
        public string? AccountingCode { get; init; }
        public string? OperatingCurrency { get; init; }
        public string? Timezone { get; init; }

        public string Status { get; init; } = "Active";
        public string? Plan { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
