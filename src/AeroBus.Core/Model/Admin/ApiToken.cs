namespace AeroBus.Core.Model.Admin
{
    public sealed class ApiToken : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string Name { get; init; } = default!;
        /// <summary>8-char human-readable identifier shown alongside the key — safe to log/display.</summary>
        public string Prefix { get; init; } = default!;
        /// <summary>SHA-256 of the secret bytes; null when returned from List endpoints (sensitive).</summary>
        public byte[]? Hash { get; init; }
        public string? Scopes { get; init; }
        public DateTime? Expires { get; init; }
        public DateTime? LastUsed { get; init; }
        public string? LastIP { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Revoked { get; init; }
    }
}
