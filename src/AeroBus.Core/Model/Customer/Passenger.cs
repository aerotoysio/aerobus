namespace AeroBus.Core.Model.Customer
{
    /// <summary>
    /// A passenger INSTANCE as booked on one order (embedded in the order
    /// aggregate — a snapshot of who travelled). <see cref="CustomerId"/> links
    /// the instance to the durable identity in the customers collection
    /// (create-or-link by email / phone + surname at order create); the copied
    /// name/contact data stays on the order as it was at booking time.
    /// </summary>
    public sealed record Passenger
    {
        public Guid Id { get; set; }
        public Guid? CustomerId { get; set; }
        public string PaxType { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string? Gender { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
        public Guid? CompanyId { get; set; }
        public Guid? ConcurrencyId { get; set; }
    }
}
