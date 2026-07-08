namespace AeroBus.Core.Model.Admin
{
    public sealed class Workspace : IDocument
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid? CreatedBy { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? BranchName { get; set; }
        public int? PrNumber { get; set; }
        public string? Status { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
    }
}
