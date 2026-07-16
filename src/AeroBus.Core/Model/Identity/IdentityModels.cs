namespace AeroBus.Core.Model.Identity
{
    /// <summary>A user as surfaced to the admin UI: Keycloak user + system roles + custom org roles.</summary>
    public sealed record OrgUser(
        string Id,
        string Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Enabled,
        IReadOnlyList<string> Roles,
        IReadOnlyList<OrgRoleRef> CustomRoles);

    public sealed record OrgRoleRef(Guid Id, string Name);

    public sealed record OrgRoleInfo(
        Guid Id,
        string Name,
        string? Description,
        IReadOnlyList<string> Permissions);

    /// <summary>GET /identity/roles: fixed Keycloak system roles + tenant-defined custom roles.</summary>
    public sealed record RolesResponse(
        IReadOnlyList<RoleInfo> System,
        IReadOnlyList<OrgRoleInfo> Custom);

    public sealed record SaveOrgRoleRequest(
        string Name,
        string? Description,
        IReadOnlyList<string> Permissions);

    /// <summary>A programmatic account (ab_ API key) as listed to the admin UI.</summary>
    public sealed record AgentInfo(
        Guid Id,
        string Name,
        string Prefix,
        IReadOnlyList<string> Permissions,
        DateTime? Created,
        DateTime? LastUsed,
        DateTime? Expires,
        bool Revoked);

    public sealed record CreateAgentRequest(
        string Name,
        IReadOnlyList<string> Permissions,
        int? ExpiresInDays);

    /// <summary>The plaintext key is shown exactly once; it is never stored.</summary>
    public sealed record CreateAgentResult(AgentInfo Agent, string Key);

    public sealed record OrganizationInfo(
        string Id,
        string Name,
        string Alias,
        bool Enabled,
        IReadOnlyList<string> Domains);

    public sealed record RoleInfo(string Name, string? Description);

    /// <summary>
    /// New-user payload. Organization (an org alias) may only be set by platform
    /// admins; org admins always create into their own organisation.
    /// </summary>
    public sealed record CreateUserRequest(
        string Email,
        string? FirstName,
        string? LastName,
        string Password,
        IReadOnlyList<string> Roles,
        string? Organization);

    public sealed record SetRolesRequest(
        IReadOnlyList<string> Roles,
        IReadOnlyList<Guid>? CustomRoleIds);

    public sealed record SetEnabledRequest(bool Enabled);

    public sealed record ResetPasswordRequest(string Password);

    /// <summary>The caller's own profile (self-service — no permission required).</summary>
    public sealed record ProfileInfo(
        string Id,
        string Username,
        string? Email,
        string? FirstName,
        string? LastName,
        string? Picture);

    public sealed record UpdateProfileRequest(string? FirstName, string? LastName);

    public sealed record ChangePasswordRequest(string Password);

    /// <summary>Avatar as a data:image/... URI; null clears it. Keycloak stores strings only, so the image lives in DocumentForge.</summary>
    public sealed record SetPictureRequest(string? Picture);

    /// <summary>The caller's organisation + site settings (stored as a Keycloak organization attribute).</summary>
    public sealed record OrganizationSettings(
        string Id,
        string Name,
        string Alias,
        IReadOnlyList<string> Domains,
        Dictionary<string, string> Settings);

    public sealed record UpdateOrganizationRequest(
        string? Name,
        Dictionary<string, string>? Settings);

    /// <summary>Self-service tenant creation from the login page (dev flow; will be gated later).
    /// The SaaS fields (ShortName onward) drive DocumentForge provisioning: the org gets its
    /// own database named by ShortName, seeded with a Company doc + reference starter pack.</summary>
    public sealed record OnboardRequest(
        string OrganizationName,
        string AdminEmail,
        string? AdminFirstName,
        string? AdminLastName,
        string Password,
        string? ShortName = null,
        string? Designator = null,
        string? AccountingCode = null,
        string? OperatingCurrency = null,
        string? Timezone = null,
        string? Plan = null);

    /// <summary>Onboarding outcome: the Keycloak org + admin, plus the provisioned tenant database.</summary>
    public sealed record OnboardResult(OrganizationInfo Organization, string AdminUserId, string? Database = null);
}
