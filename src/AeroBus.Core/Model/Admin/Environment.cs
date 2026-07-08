namespace AeroBus.Core.Model.Admin
{
    public sealed class Environment : IDocument
    {
        public Guid Id { get; init; }
        public Guid? CompanyId { get; init; }
        public string? Name { get; init; }
        public string? Branch { get; init; }
        public string? LatestCommit { get; init; }
        public DateTime? Created { get; init; }
        public string? Status { get; init; }
    }
}
