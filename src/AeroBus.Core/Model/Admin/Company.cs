namespace AeroBus.Core.Model.Admin
{
    public sealed class Company : IDocument
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Slug { get; init; }
        public string? Region { get; init; }
        public string? Status { get; init; }
        public string? Designator { get; init; }
        public string? AccountingCode { get; init; }
        public string? OperatingCurrency { get; init; }             // 3-char ISO, e.g. "AED"
        public decimal? DefaultExpectedLoadFactor { get; init; }    // e.g. 0.80 = 80%
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public List<CompanyConfig>? Configs { get; set; }
    }
}
