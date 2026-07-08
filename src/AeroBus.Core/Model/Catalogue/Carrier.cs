using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Carrier
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? Alliance { get; init; }
        public JsonNode? Hubs { get; init; }
        public string? DisplayTier { get; init; }
        public JsonNode? PreferredVia { get; init; }
        public JsonNode? AvoidVia { get; init; }
        public bool? InterlineFriendly { get; init; }
        public string? Tags { get; init; }
        public JsonNode? Data { get; init; }
        public string Status { get; init; } = "Active";
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid ConcurrencyId { get; init; }
    }
}
