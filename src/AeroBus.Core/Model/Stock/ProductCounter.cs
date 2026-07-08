namespace AeroBus.Core.Model.Stock
{
    /// <summary>
    /// A stock counter for a SKU within a bucket, scoped to a company. Lives in its
    /// OWN DocumentForge collection (not embedded in Product) because it is high-write
    /// and SKU/bucket-keyed. <see cref="Id"/> is a stable surrogate key — derive it
    /// deterministically from CompanyId+Sku+Bucket so upserts replace the same document.
    /// </summary>
    public sealed record ProductCounter : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string Sku { get; init; } = string.Empty;
        public string Bucket { get; init; } = string.Empty;
        public long? Count { get; init; }
        public DateTime? Modified { get; init; }
    }
}
