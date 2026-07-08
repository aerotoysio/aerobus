namespace AeroBus.Core.Model.Catalogue
{
    /// <summary>
    /// A custom field definition embedded in its parent <see cref="Product"/>
    /// document. (Formerly a standalone catalogue.ProductMetadata row keyed by
    /// ProductId; that parent FK is dropped now that it lives inside the product.)
    /// </summary>
    public sealed record ProductMetadata
    {
        public Guid Id { get; init; }
        public string? DataName { get; init; }
        public string? DataType { get; init; }
        public string? Options { get; init; }
        public int? Required { get; init; }
        public DateTime? Updated { get; init; }
        public DateTime? Created { get; init; }
        public string? Status { get; init; }
        public string? DataKey { get; init; }
        public string? DataDescription { get; init; }
    }
}
