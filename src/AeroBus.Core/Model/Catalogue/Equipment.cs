namespace AeroBus.Core.Model.Catalogue
{
    public sealed record Equipment : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string EquipmentCode { get; init; } = string.Empty;
        public string? Name { get; init; }
        public Guid? LayoutId { get; init; }
        public int? RangeNm { get; init; }
        public short? EtopsMinutes { get; init; }
        public string? Tags { get; init; }
        public string? Data { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
