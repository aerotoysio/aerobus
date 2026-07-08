namespace AeroBus.Core.Model.Catalogue
{
    public sealed class Media : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }

        public Guid? ParentId { get; init; }
        public string? Type { get; init; }

        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }

        public string? Locale { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public bool? AutoTranslate { get; init; }

        public string? Slug { get; init; }
        public string? MediaURL { get; init; }
        public string? ContentType { get; init; }
        public string? Status { get; init; }
        public int? Order { get; init; }

        public string? Data { get; init; }
    }
}
