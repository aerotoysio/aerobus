using System.Text.Json.Nodes;

namespace AeroBus.Core.Model.Catalogue
{
    public sealed record ConnectionRule : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? AirportCode { get; init; }
        public string? ConnType { get; init; }
        public string? FromTerminal { get; init; }
        public string? ToTerminal { get; init; }
        public string? Carrier { get; init; }
        public string? Alliance { get; init; }
        public short MinMinutes { get; init; }
        public short MaxMinutes { get; init; }
        public string? Tags { get; init; }
        public JsonNode? Data { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
