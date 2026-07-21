using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Airport : IDocument
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? City { get; init; }
        public Guid? RegionId { get; init; }
        public Guid? CountryId { get; init; }
        public string? TimeZoneId { get; init; }
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; } 
        public string? Terminals { get; init; }
        public string? Tags { get; init; }
        public JsonNode? Data { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? CompanyId { get; init; }
    }
}
