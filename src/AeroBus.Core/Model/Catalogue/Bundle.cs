using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Bundle : IDocument
    {
        public Guid Id { get; set; }
        public Guid? CompanyId { get; set; }

        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }

        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public List<Product> Products { get; } = new();
        public JsonNode? Data { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
    }
}
