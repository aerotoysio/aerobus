namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Country : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? Name { get; init; }
        public string? Code { get; init; }
        public Guid? ContinentId { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public string? Status { get; init; }
    }
}
