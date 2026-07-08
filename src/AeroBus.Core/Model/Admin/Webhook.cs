namespace AeroBus.Core.Model.Admin
{
    public sealed class Webhook
    {
        public Guid? Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? Url { get; init; }
        public string? Secret { get; init; }
        public string? Events { get; init; }
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
    }
}
