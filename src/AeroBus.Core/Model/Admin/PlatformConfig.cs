namespace AeroBus.Core.Model.Admin
{
    /// <summary>
    /// A platform-level configuration entry, stored in the CONTROL database
    /// (settings live in the database, not appsettings — the only bootstrap
    /// settings left in appsettings are how to reach Keycloak and DocumentForge).
    /// Logically keyed by <see cref="Key"/> (dot-namespaced, e.g.
    /// <c>ruleforge.baseUrl</c>); the document <see cref="Id"/> is a
    /// deterministic surrogate of the key so an upsert replaces in place.
    /// Secret values (<see cref="IsSecret"/>) are encrypted at rest via Data
    /// Protection and never returned in plaintext by the admin API.
    /// </summary>
    public sealed record PlatformConfig : IDocument
    {
        public Guid Id { get; init; }
        public string? Key { get; init; }

        /// <summary>Plaintext for normal entries; the Data-Protection-encrypted payload for secrets.</summary>
        public string? Value { get; init; }

        public bool IsSecret { get; init; }
        public string? Description { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
