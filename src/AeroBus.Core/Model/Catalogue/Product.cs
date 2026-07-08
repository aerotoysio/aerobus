using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Product : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? Category { get; init; }
        public string? ProductType { get; init; }
        public string? Code { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public JsonNode? Data { get; init; }
        public decimal? CostAmount { get; init; }              // direct cost per product unit
        public string? CostCurrency { get; init; }             // 3-char ISO; null inherits Company.OperatingCurrency
        public string? Tags { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }

        /// <summary>Custom field definitions for this product, folded in from the
        /// former catalogue.ProductMetadata table — the product is now one document.</summary>
        public List<ProductMetadata>? Metadata { get; init; }
    }
}
