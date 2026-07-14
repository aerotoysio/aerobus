namespace AeroBus.Core.Identity
{
    /// <summary>
    /// Keycloak connection settings, bound from the <c>Keycloak</c> configuration
    /// section (<c>Keycloak__BaseUrl</c> etc. in the environment). When the section
    /// is absent the Keycloak authentication scheme and the /identity module are
    /// registered but inert — the self-issued JWT and ab_ API-key paths are unaffected.
    /// </summary>
    public sealed class KeycloakOptions
    {
        public const string SectionName = "Keycloak";

        /// <summary>Authentication scheme name for Keycloak-issued bearer tokens.</summary>
        public const string Scheme = "Keycloak";

        public string BaseUrl { get; set; } = string.Empty;

        public string Realm { get; set; } = string.Empty;

        /// <summary>Confidential client with a service account that holds realm-management roles.</summary>
        public string ClientId { get; set; } = "aerobus";

        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>Expected <c>aud</c> in access tokens (set by the aerobus-aud client scope).</summary>
        public string Audience { get; set; } = "aerobus";

        public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Realm);

        /// <summary>OIDC issuer, e.g. <c>http://localhost:8080/realms/aerotoys</c>.</summary>
        public string Authority => $"{BaseUrl.TrimEnd('/')}/realms/{Realm}";

        /// <summary>Admin REST base, e.g. <c>http://localhost:8080/admin/realms/aerotoys</c>.</summary>
        public string AdminBase => $"{BaseUrl.TrimEnd('/')}/admin/realms/{Realm}";
    }
}
