using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Admin
{
    public sealed record UserPermission
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public Guid UserId { get; init; }
        public string Resource { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Effect { get; init; } = string.Empty;
        public string? Tags { get; init; }
        public JsonNode? Data { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid ConcurrencyId { get; init; }
    }
}
