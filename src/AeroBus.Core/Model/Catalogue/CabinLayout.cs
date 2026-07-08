namespace AeroBus.Core.Model.Catalogue
{
    public sealed record CabinLayout
    {
        public Guid Id { get; init; }
        public Guid LayoutId { get; init; }
        public Guid CabinId { get; init; }
        public int StartingRow { get; init; }
        public int EndingRow { get; init; }
        public int Rows { get; init; }
        public int Columns { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? ConcurrencyId { get; init; }
        public Guid? CompanyId { get; init; }
    }
}
