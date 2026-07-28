namespace AeroBus.Core.Model.Catalogue
{
    /// <summary>
    /// A Right to Fly — the fare brand (Saver / Flex / Business): the right to
    /// purchase a seat on a flight solution. Carries three rule attachments
    /// (eligibility, pricing, benefits) and the connected Policy rules whose
    /// products it grants; aerobus orchestrates those policies directly at
    /// benefits time (docs/rule-based-retailing.md). Replaces bundles as the
    /// brand row on shopping — Bundles remain product packages only.
    /// </summary>
    public sealed record RightToFly : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? RtfCode { get; init; }
        public string? RtfName { get; init; }
        public string? Description { get; init; }
        public int Rank { get; init; }
        public string? EligibilityRuleId { get; init; }
        public string? PricingRuleId { get; init; }
        public string? BenefitsRuleId { get; init; }
        public IReadOnlyList<string>? PolicyIds { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public string? Status { get; init; }
    }
}
