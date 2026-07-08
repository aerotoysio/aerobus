namespace AeroBus.Core.Model.Admin
{
    /// <summary>
    /// A per-company configuration entry. Logically keyed by (CompanyId, Key); the
    /// document <see cref="Id"/> is a deterministic surrogate derived from those two
    /// (see CompanyConfigs repo) so an upsert of the same company+key replaces in place.
    /// </summary>
    public sealed record CompanyConfig : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? Key { get; init; }
        public string? Value { get; init; }
        public string? Description { get; init; }
    }
}
