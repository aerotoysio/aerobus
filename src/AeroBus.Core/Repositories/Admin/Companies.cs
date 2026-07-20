using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    public interface ICompanies
    {
        Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Company?> GetBySlugAsync(string slug, CancellationToken ct = default);
        Task<IReadOnlyList<Company>> GetAllAsync(CancellationToken ct = default);
        Task<Company?> SaveAsync(Company model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public sealed class Companies(IDocumentStore store) : DocumentRepository<Company>(store), ICompanies
    {
        protected override string Collection => DfCollections.Admin.Companies;

        // Bespoke: enrich the company with its embedded configs. The
        // companyconfigs collection is composite-keyed (CompanyId+Key) and is
        // queried by field here — cross-collection read.
        public override async Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            var company = await base.GetByIdAsync(id, ct);
            if (company is null)
                return null;

            var configs = await Store.QueryAsync<CompanyConfig>(
                DfCollections.Admin.CompanyConfigs, Eq("companyId", id), ct: ct);

            company.Configs = configs.ToList();

            return company;
        }

        public Task<Company?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
            GetByFieldAsync("slug", slug, ct);

        public Task<IReadOnlyList<Company>> GetAllAsync(CancellationToken ct = default) =>
            ListAsync(ct: ct);
    }
}
