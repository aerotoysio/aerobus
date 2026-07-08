using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Admin
{
    public sealed class Permission
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? Tags { get; init; }
        public JsonNode? Data { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid ConcurrencyId { get; init; }
    }
}
