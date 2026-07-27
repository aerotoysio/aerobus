namespace AeroBus.Core.Model.Catalogue
{
    /// <summary>
    /// A loyalty programme tier (e.g. Blue, Silver, Gold). Customers reference
    /// tiers by <see cref="TierCode"/>; rules filter on that code to price
    /// tier-specific offers. <see cref="Rank"/> orders tiers lowest → highest.
    /// </summary>
    public sealed record LoyaltyTier : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? TierCode { get; init; }
        public string? TierName { get; init; }
        public string? Description { get; init; }
        public int Rank { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public string? Status { get; init; }
    }
}
