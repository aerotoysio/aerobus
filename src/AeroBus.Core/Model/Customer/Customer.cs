namespace AeroBus.Core.Model.Customer
{
    // Customer = the account holder (loyalty / saved data) as one document.
    // Saved travel documents and payment cards are embedded; passengers are
    // booking-scoped and live on the Order document instead.

    public sealed record Customer
    {
        public Guid Id { get; set; }
        public Guid? CompanyId { get; set; }
        public string? CustomerNumber { get; set; }
        public string? Title { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? LoyaltyProgram { get; set; }
        public string? LoyaltyNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
        public Guid? ConcurrencyId { get; set; }

        public List<Passport>? Passports { get; set; }
        public List<StoredCard>? StoredCards { get; set; }
    }

    public sealed record Passport
    {
        public Guid Id { get; init; }
        public string CountryCode { get; init; } = string.Empty;
        public string PassportNumber { get; init; } = string.Empty;
        public DateTime ExpiryDate { get; init; }
        public string? Nationality { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime? CreatedAtUtc { get; init; }
        public DateTime? UpdatedAtUtc { get; init; }
    }

    public sealed record StoredCard
    {
        public string Id { get; init; } = string.Empty;
        public string? CardToken { get; init; }
        public string? Provider { get; init; }
        public string? LastFour { get; init; }
        public DateTime? Created { get; init; }
        public DateTime? Updated { get; init; }
        public string? Status { get; init; }
    }
}
