namespace AeroBus.Core.Model.Customer
{
    public sealed record Passenger
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid? CustomerId { get; set; }
        public string PaxType { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string? Gender { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
        public Guid? CompanyId { get; set; }
        public Guid? ConcurrencyId { get; set; }
    }
}
