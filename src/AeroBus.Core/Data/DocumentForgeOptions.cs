namespace AeroBus.Core.Data
{
    /// <summary>
    /// Connection settings for the DocumentForge datastore, bound from the
    /// <c>DocumentForge</c> configuration section (<c>DocumentForge__BaseUrl</c> /
    /// <c>DocumentForge__ApiKey</c> in the environment).
    /// </summary>
    public sealed class DocumentForgeOptions
    {
        public const string SectionName = "DocumentForge";

        public string BaseUrl { get; set; } = "http://localhost:4300";

        /// <summary>Bearer key; empty for an --insecure-dev-mode dfdb.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Named DocumentForge database to store business data in (e.g.
        /// <c>aerotoys</c> → <c>aerotoys.dfdb</c> on a multi-database server).
        /// The client then talks to the scoped <c>/db/{name}/…</c> surface.
        /// Null/empty targets the server's default database over the flat routes.
        /// Rules authoring always stays on the default database — that is where
        /// RuleForge reads its rules from.
        /// </summary>
        public string? Database { get; set; }

        /// <summary>
        /// The named database holding the SHARED control plane (tenant registry,
        /// identity/RBAC, API tokens, events outbox). Created automatically at
        /// startup. A named database keeps every control call on DocumentForge's
        /// scoped <c>db/{name}</c> surface, where namespaced collection names are
        /// fully supported on all deployed server versions.
        /// </summary>
        public string ControlDatabase { get; set; } = "control";
    }
}
