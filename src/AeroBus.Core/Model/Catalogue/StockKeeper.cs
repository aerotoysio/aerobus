namespace AeroBus.Core.Model.Catalogue
{
    public sealed record StockKeeper : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? Definition { get; init; }
        public string? Type { get; init; }
        public string? Scope { get; init; }
        public string Status { get; init; } = "Active";
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
