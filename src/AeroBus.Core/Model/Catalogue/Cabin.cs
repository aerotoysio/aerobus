namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Cabin
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? ConcurrencyId { get; init; }
        public Guid? CompanyId { get; init; }
    }
}
