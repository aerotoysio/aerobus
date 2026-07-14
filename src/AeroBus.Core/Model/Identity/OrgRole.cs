using AeroBus.Core.Model;

namespace AeroBus.Core.Model.Identity
{
    /// <summary>
    /// A tenant-defined role: a named bundle of permission codes from the
    /// PermissionCatalog, scoped to one organisation (CompanyId = the Keycloak
    /// organization id). Complements the fixed Keycloak system roles
    /// (platform-admin / org-admin / editor / viewer).
    /// </summary>
    public sealed record OrgRole : IDocument
    {
        public Guid Id { get; init; }
        public Guid CompanyId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public List<string> Permissions { get; init; } = [];
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }

    /// <summary>
    /// Custom-role assignments for one user. Document id IS the Keycloak user
    /// id (a GUID), so lookup at token-validation time is a single get-by-id.
    /// </summary>
    public sealed record OrgRoleAssignment : IDocument
    {
        public Guid Id { get; init; } // Keycloak user id
        public Guid CompanyId { get; init; }
        public List<Guid> RoleIds { get; init; } = [];
        public DateTime? Updated { get; init; }
    }

    /// <summary>
    /// Per-user profile extras that don't belong in Keycloak (which stores
    /// string attributes only): currently the avatar as a data URI. Document
    /// id IS the Keycloak user id.
    /// </summary>
    public sealed record UserProfileDoc : IDocument
    {
        public Guid Id { get; init; } // Keycloak user id
        public Guid CompanyId { get; init; }
        public string? Picture { get; init; } // data:image/... URI
        public DateTime? Updated { get; init; }
    }
}
