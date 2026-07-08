namespace AeroBus.Core.Model.Catalogue
{
    public sealed class Attribute : IDocument
    {
        public Guid Id { get; init; }
        public Guid? ParentId { get; init; }
        public Guid? CompanyId { get; init; }

        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }

        public string? Key { get; init; }
        public string? Name { get; init; }
        public string? Value { get; init; }
        public string? Icon { get; init; }
        public string? Status { get; init; }
    }
}
