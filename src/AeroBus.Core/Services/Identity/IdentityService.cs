using System.Security.Claims;
using System.Text.RegularExpressions;
using AeroBus.Core.Identity;
using AeroBus.Core.Model.Identity;
using AeroBus.Core.Repositories.Identity;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Admin;

namespace AeroBus.Core.Services.Identity
{
    /// <summary>
    /// Raised for caller-visible identity failures; Status maps straight to the
    /// HTTP response (400 validation, 403 scope, 404 unknown, 409 conflict).
    /// </summary>
    public sealed class IdentityException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }

    /// <summary>
    /// Org-scoped identity management: Keycloak users + system roles, custom org
    /// roles (aerobus-owned bundles of PermissionCatalog codes), assignments, and
    /// programmatic agent accounts (ab_ API keys). Scoping rule: platform admins
    /// (perm admin.all) may act on any organisation; everyone else acts inside the
    /// organisation(s) carried in their token. The UI never talks to Keycloak —
    /// everything goes through here.
    /// </summary>
    public sealed partial class IdentityService(
        KeycloakAdminClient kc,
        IOrgRoles orgRoles,
        IOrgRoleAssignments assignments,
        IUserProfiles profiles,
        ApiTokenService apiTokens,
        RbacCacheVersion rbacVersion)
    {
        /// <summary>Org attribute that carries the aerostudio site settings as JSON.</summary>
        private const string SettingsAttribute = "aerostudio.settings";

        /// <summary>System roles an org admin may grant. Platform admins may also grant platform-admin.</summary>
        private static readonly string[] OrgAssignableRoles = ["org-admin", "editor", "viewer"];

        private const string PlatformAdminRole = "platform-admin";

        // ---- caller scope ------------------------------------------------------

        private static bool IsPlatformAdmin(ClaimsPrincipal caller) =>
            caller.HasClaim("perm", "admin.all");

        private static string[] CallerOrgs(ClaimsPrincipal caller) =>
            caller.FindAll("organization").Select(c => c.Value).ToArray();

        private static string CallerUserId(ClaimsPrincipal caller) =>
            caller.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? caller.FindFirst("sub")?.Value
            ?? string.Empty;

        /// <summary>
        /// Resolves the organisation the caller is operating on. Platform admins may
        /// name any org (and must name one); org members may only name their own.
        /// </summary>
        private async Task<KcOrganization> ResolveOrgAsync(ClaimsPrincipal caller, string? requestedAlias, CancellationToken ct)
        {
            string alias;
            if (IsPlatformAdmin(caller))
            {
                alias = requestedAlias
                    ?? CallerOrgs(caller).FirstOrDefault()
                    ?? throw new IdentityException(400, "Platform admins must specify an organisation (?org=alias).");
            }
            else
            {
                var orgs = CallerOrgs(caller);
                if (orgs.Length == 0)
                    throw new IdentityException(403, "Caller belongs to no organisation.");
                alias = requestedAlias ?? orgs[0];
                if (!orgs.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    throw new IdentityException(403, $"Caller is not a member of organisation '{alias}'.");
            }

            return await kc.GetOrganizationByAliasAsync(alias, ct)
                ?? throw new IdentityException(404, $"Organisation '{alias}' not found.");
        }

        private async Task<KcUser> ResolveMemberAsync(ClaimsPrincipal caller, string? orgAlias, string userId, CancellationToken ct)
        {
            var org = await ResolveOrgAsync(caller, orgAlias, ct);
            var members = await kc.GetOrganizationMembersAsync(org.Id, ct);
            return members.FirstOrDefault(m => m.Id == userId)
                ?? throw new IdentityException(404, $"User is not a member of organisation '{org.Alias}'.");
        }

        // ---- queries -----------------------------------------------------------

        public async Task<IReadOnlyList<OrgUser>> ListUsersAsync(ClaimsPrincipal caller, string? orgAlias, CancellationToken ct = default)
        {
            var org = await ResolveOrgAsync(caller, orgAlias, ct);
            var orgId = Guid.Parse(org.Id);
            var members = await kc.GetOrganizationMembersAsync(org.Id, ct);
            var customById = (await orgRoles.GetByCompanyAsync(orgId, ct)).ToDictionary(r => r.Id);

            var result = new List<OrgUser>(members.Count);
            foreach (var m in members)
            {
                var roles = await kc.GetUserRealmRolesAsync(m.Id, ct);
                var custom = Guid.TryParse(m.Id, out var uid)
                    ? await CustomRolesOfAsync(uid, orgId, customById, ct)
                    : [];
                result.Add(ToOrgUser(m, roles, custom));
            }
            return result;
        }

        public async Task<RolesResponse> ListRolesAsync(ClaimsPrincipal caller, CancellationToken ct = default)
        {
            var assignable = AssignableBy(caller);
            var realmRoles = await kc.ListRealmRolesAsync(ct);
            var system = realmRoles
                .Where(r => assignable.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                .Select(r => new RoleInfo(r.Name, r.Description))
                .OrderBy(r => r.Name)
                .ToList();

            var org = await ResolveOrgAsync(caller, null, ct);
            var custom = (await orgRoles.GetByCompanyAsync(Guid.Parse(org.Id), ct))
                .Select(ToOrgRoleInfo)
                .OrderBy(r => r.Name)
                .ToList();

            return new RolesResponse(system, custom);
        }

        public async Task<IReadOnlyList<OrganizationInfo>> ListOrganizationsAsync(CancellationToken ct = default) =>
            (await kc.ListOrganizationsAsync(ct)).Select(ToOrgInfo).OrderBy(o => o.Name).ToList();

        // ---- custom org roles --------------------------------------------------

        public async Task<OrgRoleInfo> CreateOrgRoleAsync(ClaimsPrincipal caller, SaveOrgRoleRequest req, CancellationToken ct = default)
        {
            var org = await ResolveOrgAsync(caller, null, ct);
            var orgId = Guid.Parse(org.Id);
            ValidateOrgRole(req);

            var existing = await orgRoles.GetByCompanyAsync(orgId, ct);
            if (existing.Any(r => r.Name.Equals(req.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new IdentityException(409, $"A role named '{req.Name.Trim()}' already exists.");

            var saved = await orgRoles.SaveAsync(new OrgRole
            {
                Id = Guid.NewGuid(),
                CompanyId = orgId,
                Name = req.Name.Trim(),
                Description = req.Description,
                Permissions = NormalisePermissions(req.Permissions),
                Created = DateTime.UtcNow,
            }, ct) ?? throw new IdentityException(502, "Failed to persist the role.");

            rbacVersion.Bump();
            return ToOrgRoleInfo(saved);
        }

        public async Task<OrgRoleInfo> UpdateOrgRoleAsync(ClaimsPrincipal caller, Guid roleId, SaveOrgRoleRequest req, CancellationToken ct = default)
        {
            var role = await OwnedRoleAsync(caller, roleId, ct);
            ValidateOrgRole(req);

            var saved = await orgRoles.SaveAsync(role with
            {
                Name = req.Name.Trim(),
                Description = req.Description,
                Permissions = NormalisePermissions(req.Permissions),
                Updated = DateTime.UtcNow,
            }, ct) ?? throw new IdentityException(502, "Failed to persist the role.");

            rbacVersion.Bump();
            return ToOrgRoleInfo(saved);
        }

        public async Task DeleteOrgRoleAsync(ClaimsPrincipal caller, Guid roleId, CancellationToken ct = default)
        {
            var role = await OwnedRoleAsync(caller, roleId, ct);

            // Strip the role from every assignment in the org before deleting it.
            foreach (var assignment in await assignments.GetByCompanyAsync(role.CompanyId, ct))
            {
                if (!assignment.RoleIds.Contains(roleId)) continue;
                await assignments.SaveAsync(assignment with
                {
                    RoleIds = assignment.RoleIds.Where(id => id != roleId).ToList(),
                    Updated = DateTime.UtcNow,
                }, ct);
            }

            await orgRoles.DeleteAsync(roleId, ct);
            rbacVersion.Bump();
        }

        // ---- commands ----------------------------------------------------------

        public async Task<OrgUser> CreateUserAsync(ClaimsPrincipal caller, CreateUserRequest req, CancellationToken ct = default)
        {
            ValidateEmail(req.Email);
            ValidatePassword(req.Password);
            var roles = ValidateSystemRoles(caller, req.Roles);

            var org = await ResolveOrgAsync(caller, req.Organization, ct);

            if (await kc.GetUserByUsernameAsync(req.Email, ct) is not null)
                throw new IdentityException(409, $"A user with email '{req.Email}' already exists.");

            var user = await kc.CreateUserAsync(req.Email, req.FirstName, req.LastName, req.Password, ct);
            await kc.AddOrganizationMemberAsync(org.Id, user.Id, ct);
            await SetManagedRolesAsync(user.Id, roles, ct);

            return ToOrgUser(user, await kc.GetUserRealmRolesAsync(user.Id, ct), []);
        }

        public async Task<OrgUser> SetRolesAsync(ClaimsPrincipal caller, string userId, SetRolesRequest req, string? orgAlias, CancellationToken ct = default)
        {
            if (userId == CallerUserId(caller))
                throw new IdentityException(403, "You cannot change your own roles.");

            var roles = ValidateSystemRoles(caller, req.Roles);
            var member = await ResolveMemberAsync(caller, orgAlias, userId, ct);
            var org = await ResolveOrgAsync(caller, orgAlias, ct);
            var orgId = Guid.Parse(org.Id);

            await SetManagedRolesAsync(member.Id, roles, ct);

            // Custom roles: null leaves the assignment untouched; [] clears it.
            var customById = (await orgRoles.GetByCompanyAsync(orgId, ct)).ToDictionary(r => r.Id);
            if (req.CustomRoleIds is not null)
            {
                var unknown = req.CustomRoleIds.Where(id => !customById.ContainsKey(id)).ToList();
                if (unknown.Count > 0)
                    throw new IdentityException(400, "One or more custom roles do not exist in this organisation.");

                var userGuid = Guid.Parse(member.Id);
                await assignments.SaveAsync(new OrgRoleAssignment
                {
                    Id = userGuid,
                    CompanyId = orgId,
                    RoleIds = req.CustomRoleIds.Distinct().ToList(),
                    Updated = DateTime.UtcNow,
                }, ct);
                rbacVersion.Bump();
            }

            var custom = await CustomRolesOfAsync(Guid.Parse(member.Id), orgId, customById, ct);
            return ToOrgUser(member, await kc.GetUserRealmRolesAsync(member.Id, ct), custom);
        }

        public async Task SetEnabledAsync(ClaimsPrincipal caller, string userId, bool enabled, string? orgAlias, CancellationToken ct = default)
        {
            if (userId == CallerUserId(caller))
                throw new IdentityException(403, "You cannot disable your own account.");

            var member = await ResolveMemberAsync(caller, orgAlias, userId, ct);
            await kc.SetUserEnabledAsync(member.Id, enabled, ct);
        }

        public async Task ResetPasswordAsync(ClaimsPrincipal caller, string userId, string password, string? orgAlias, CancellationToken ct = default)
        {
            ValidatePassword(password);
            var member = await ResolveMemberAsync(caller, orgAlias, userId, ct);
            await kc.ResetPasswordAsync(member.Id, password, ct);
        }

        /// <summary>
        /// Self-service tenant creation: organisation + its first org-admin user.
        /// Anonymous by design for now (login-page flow); gate before production.
        /// </summary>
        public async Task<OnboardResult> OnboardAsync(OnboardRequest req, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(req.OrganizationName))
                throw new IdentityException(400, "Organisation name is required.");
            ValidateEmail(req.AdminEmail);
            ValidatePassword(req.Password);

            var alias = Slugify(req.OrganizationName);
            if (alias.Length < 2)
                throw new IdentityException(400, "Organisation name must contain at least two letters or digits.");

            if (await kc.GetOrganizationByAliasAsync(alias, ct) is not null)
                throw new IdentityException(409, $"Organisation '{alias}' already exists.");
            if (await kc.GetUserByUsernameAsync(req.AdminEmail, ct) is not null)
                throw new IdentityException(409, $"A user with email '{req.AdminEmail}' already exists.");

            var org = await kc.CreateOrganizationAsync(req.OrganizationName.Trim(), alias, $"{alias}.aerotoys.local", ct);
            var user = await kc.CreateUserAsync(req.AdminEmail, req.AdminFirstName, req.AdminLastName, req.Password, ct);
            await kc.AddOrganizationMemberAsync(org.Id, user.Id, ct);
            await SetManagedRolesAsync(user.Id, ["org-admin", "editor"], ct);

            return new OnboardResult(ToOrgInfo(org), user.Id);
        }

        // ---- self-service profile ----------------------------------------------

        private Guid RequireCallerId(ClaimsPrincipal caller) =>
            Guid.TryParse(CallerUserId(caller), out var id)
                ? id
                : throw new IdentityException(403, "Only interactive users have a profile.");

        private static Guid CallerCompanyId(ClaimsPrincipal caller) =>
            Guid.TryParse(caller.FindFirst("companyId")?.Value, out var id) ? id : Guid.Empty;

        public async Task<ProfileInfo> GetProfileAsync(ClaimsPrincipal caller, CancellationToken ct = default)
        {
            var id = RequireCallerId(caller);
            var user = await kc.GetUserByIdAsync(id.ToString(), ct);
            var doc = await profiles.GetByUserAsync(id, ct);
            return new ProfileInfo(user.Id, user.Username, user.Email, user.FirstName, user.LastName, doc?.Picture);
        }

        public async Task UpdateProfileAsync(ClaimsPrincipal caller, UpdateProfileRequest req, CancellationToken ct = default)
        {
            var id = RequireCallerId(caller);
            await kc.UpdateUserNamesAsync(id.ToString(), req.FirstName?.Trim(), req.LastName?.Trim(), ct);
        }

        public async Task ChangeOwnPasswordAsync(ClaimsPrincipal caller, string password, CancellationToken ct = default)
        {
            ValidatePassword(password);
            var id = RequireCallerId(caller);
            await kc.ResetPasswordAsync(id.ToString(), password, ct);
        }

        public async Task SetPictureAsync(ClaimsPrincipal caller, string? picture, CancellationToken ct = default)
        {
            var id = RequireCallerId(caller);
            if (picture is not null)
            {
                if (!picture.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                    throw new IdentityException(400, "Picture must be a data:image/... URI.");
                if (picture.Length > 400_000)
                    throw new IdentityException(400, "Picture too large — keep it under ~300 KB.");
            }
            await profiles.SaveAsync(new UserProfileDoc
            {
                Id = id,
                CompanyId = CallerCompanyId(caller),
                Picture = picture,
                Updated = DateTime.UtcNow,
            }, ct);
        }

        // ---- organisation profile + site settings -------------------------------

        public async Task<OrganizationSettings> GetOrganizationSettingsAsync(ClaimsPrincipal caller, CancellationToken ct = default)
        {
            var org = await ResolveOrgAsync(caller, null, ct);
            // The search endpoint omits attributes — fetch the full representation.
            return ToOrgSettings(await kc.GetOrganizationAsync(org.Id, ct));
        }

        public async Task<OrganizationSettings> UpdateOrganizationSettingsAsync(
            ClaimsPrincipal caller, UpdateOrganizationRequest req, CancellationToken ct = default)
        {
            var resolved = await ResolveOrgAsync(caller, null, ct);
            var org = await kc.GetOrganizationAsync(resolved.Id, ct);

            var name = string.IsNullOrWhiteSpace(req.Name) ? org.Name : req.Name.Trim();
            var attributes = org.Attributes ?? [];
            if (req.Settings is not null)
                attributes[SettingsAttribute] = [System.Text.Json.JsonSerializer.Serialize(req.Settings)];

            await kc.UpdateOrganizationAsync(org.Id, new
            {
                name,
                alias = org.Alias,
                enabled = org.Enabled,
                domains = org.Domains,
                attributes,
            }, ct);

            return ToOrgSettings(await kc.GetOrganizationAsync(org.Id, ct));
        }

        private static OrganizationSettings ToOrgSettings(KcOrganization o)
        {
            var settings = new Dictionary<string, string>();
            if (o.Attributes?.TryGetValue(SettingsAttribute, out var raw) == true && raw.Count > 0)
            {
                try
                {
                    settings = System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, string>>(raw[0]) ?? [];
                }
                catch
                {
                    // Corrupt settings attribute — surface as empty rather than failing the page.
                }
            }
            return new OrganizationSettings(
                o.Id, o.Name, o.Alias ?? o.Name,
                o.Domains?.Select(d => d.Name).ToList() ?? [],
                settings);
        }

        // ---- agents (programmatic accounts) ------------------------------------

        public async Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(ClaimsPrincipal caller, string? orgAlias, CancellationToken ct = default)
        {
            var org = await ResolveOrgAsync(caller, orgAlias, ct);
            var rows = await apiTokens.ListByCompanyAsync(Guid.Parse(org.Id), ct);
            return rows.Select(ToAgentInfo).OrderBy(a => a.Name).ToList();
        }

        public async Task<CreateAgentResult> CreateAgentAsync(ClaimsPrincipal caller, CreateAgentRequest req, string? orgAlias, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new IdentityException(400, "Agent name is required.");
            var perms = NormalisePermissions(req.Permissions);
            if (perms.Count == 0)
                throw new IdentityException(400, "At least one permission is required.");

            var org = await ResolveOrgAsync(caller, orgAlias, ct);
            var expires = req.ExpiresInDays is { } days and > 0 ? DateTime.UtcNow.AddDays(days) : (DateTime?)null;

            var created = await apiTokens.GenerateAsync(Guid.Parse(org.Id), req.Name, string.Join(",", perms), expires, ct);
            return new CreateAgentResult(ToAgentInfo(created.Record), created.Plaintext);
        }

        public async Task RevokeAgentAsync(ClaimsPrincipal caller, Guid agentId, string? orgAlias, CancellationToken ct = default)
        {
            var org = await ResolveOrgAsync(caller, orgAlias, ct);
            var rows = await apiTokens.ListByCompanyAsync(Guid.Parse(org.Id), ct);
            if (rows.All(t => t.Id != agentId))
                throw new IdentityException(404, "No such agent in this organisation.");
            await apiTokens.RevokeAsync(agentId, ct);
        }

        // ---- helpers -----------------------------------------------------------

        private async Task<OrgRole> OwnedRoleAsync(ClaimsPrincipal caller, Guid roleId, CancellationToken ct)
        {
            var org = await ResolveOrgAsync(caller, null, ct);
            var role = await orgRoles.GetByIdAsync(roleId, ct);
            if (role is null || role.CompanyId != Guid.Parse(org.Id))
                throw new IdentityException(404, "Role not found in this organisation.");
            return role;
        }

        private async Task<IReadOnlyList<OrgRoleRef>> CustomRolesOfAsync(
            Guid userId, Guid orgId, IReadOnlyDictionary<Guid, OrgRole> customById, CancellationToken ct)
        {
            var assignment = await assignments.GetByUserAsync(userId, ct);
            if (assignment is null || assignment.CompanyId != orgId) return [];
            return assignment.RoleIds
                .Where(customById.ContainsKey)
                .Select(id => new OrgRoleRef(id, customById[id].Name))
                .ToList();
        }

        private static void ValidateOrgRole(SaveOrgRoleRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new IdentityException(400, "Role name is required.");
            if (req.Permissions is null || req.Permissions.Count == 0)
                throw new IdentityException(400, "At least one permission is required.");
        }

        /// <summary>Dedupe + validate permission codes against the catalog (admin.all is never assignable here).</summary>
        private static List<string> NormalisePermissions(IReadOnlyList<string>? requested)
        {
            var perms = (requested ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var illegal = perms.Where(p => !PermissionCatalog.Exists(p)).ToList();
            if (illegal.Count > 0)
                throw new IdentityException(400, $"Unknown permission(s): {string.Join(", ", illegal)}.");
            return perms;
        }

        private string[] AssignableBy(ClaimsPrincipal caller) =>
            IsPlatformAdmin(caller) ? [.. OrgAssignableRoles, PlatformAdminRole] : OrgAssignableRoles;

        private string[] ValidateSystemRoles(ClaimsPrincipal caller, IReadOnlyList<string>? requested)
        {
            var roles = (requested ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (roles.Length == 0)
                throw new IdentityException(400, "At least one system role is required.");

            var assignable = AssignableBy(caller);
            var illegal = roles.Except(assignable, StringComparer.OrdinalIgnoreCase).ToArray();
            if (illegal.Length > 0)
                throw new IdentityException(403, $"Role(s) not assignable: {string.Join(", ", illegal)}.");
            return roles;
        }

        /// <summary>
        /// Reconciles the user's realm roles against the requested set, touching only
        /// the roles this service manages — Keycloak built-ins (default-roles-*,
        /// offline_access, uma_authorization) are left alone.
        /// </summary>
        private async Task SetManagedRolesAsync(string userId, string[] target, CancellationToken ct)
        {
            var managed = new HashSet<string>([.. OrgAssignableRoles, PlatformAdminRole], StringComparer.OrdinalIgnoreCase);
            var all = await kc.ListRealmRolesAsync(ct);
            var current = await kc.GetUserRealmRolesAsync(userId, ct);

            var toAdd = target
                .Where(r => !current.Any(c => c.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                .Select(r => all.FirstOrDefault(a => a.Name.Equals(r, StringComparison.OrdinalIgnoreCase))
                    ?? throw new IdentityException(400, $"Role '{r}' does not exist in the realm."))
                .ToList();

            var toRemove = current
                .Where(c => managed.Contains(c.Name) && !target.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (toAdd.Count > 0) await kc.AddUserRealmRolesAsync(userId, toAdd, ct);
            if (toRemove.Count > 0) await kc.RemoveUserRealmRolesAsync(userId, toRemove, ct);
        }

        private static OrgUser ToOrgUser(KcUser u, IEnumerable<KcRole> roles, IReadOnlyList<OrgRoleRef> custom)
        {
            var managed = new HashSet<string>([.. OrgAssignableRoles, PlatformAdminRole], StringComparer.OrdinalIgnoreCase);
            return new OrgUser(
                u.Id, u.Username, u.Email, u.FirstName, u.LastName, u.Enabled,
                roles.Select(r => r.Name).Where(managed.Contains).OrderBy(r => r).ToList(),
                custom);
        }

        private static OrgRoleInfo ToOrgRoleInfo(OrgRole r) =>
            new(r.Id, r.Name, r.Description, r.Permissions);

        private static AgentInfo ToAgentInfo(Model.Admin.ApiToken t) => new(
            t.Id,
            t.Name,
            t.Prefix,
            (t.Scopes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            t.Created,
            t.LastUsed,
            t.Expires,
            t.Revoked is not null);

        private static OrganizationInfo ToOrgInfo(KcOrganization o) => new(
            o.Id, o.Name, o.Alias ?? o.Name, o.Enabled,
            o.Domains?.Select(d => d.Name).ToList() ?? []);

        private static void ValidateEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Contains(' '))
                throw new IdentityException(400, "A valid email address is required.");
        }

        private static void ValidatePassword(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
                throw new IdentityException(400, "Password must be at least 8 characters.");
        }

        private static string Slugify(string name)
        {
            var slug = SlugPattern().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
            return Regex.Replace(slug, "-{2,}", "-");
        }

        [GeneratedRegex("[^a-z0-9]+")]
        private static partial Regex SlugPattern();
    }
}
