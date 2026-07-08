namespace AeroBus.Core.Model
{
    /// <summary>
    /// Marker for a top-level document (aggregate root) persisted in DocumentForge.
    /// <see cref="Id"/> is the business key, stored as the document's "Id" field.
    ///
    /// Tenant scoping (CompanyId) is intentionally NOT on this interface: it's
    /// queried by field name where present, and global reference data such as
    /// countries/continents legitimately has no company. Keeping the marker to
    /// just <see cref="Id"/> lets every model implement it without changing the
    /// nullability of its CompanyId.
    /// </summary>
    public interface IDocument
    {
        Guid Id { get; }
    }
}
