using AeroBus.Core.Common;
using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Repositories.Admin
{
    public interface ICompanyConfigs
    {
        Task<CompanyConfig?> GetByIdAsync(Guid companyId, string key, CancellationToken ct = default);
        Task<IReadOnlyList<CompanyConfig>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<CompanyConfig?> SaveAsync(CompanyConfig model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid companyId, string key, CancellationToken ct = default);
    }

    public sealed class CompanyConfigs(IDocumentStore store) : DocumentRepository<CompanyConfig>(store), ICompanyConfigs
    {
        protected override string Collection => DfCollections.Admin.CompanyConfigs;

        // CompanyConfig has a composite logical key (CompanyId, Key). We derive a stable
        // surrogate Guid from it so saves are idempotent and key lookups are direct.
        private static Guid KeyId(Guid companyId, string? key) =>
            DeterministicGuid.FromString($"{companyId:N}:{key}");

        // GetByCompanyAsync(companyId) comes from DocumentRepository<CompanyConfig>.

        public Task<CompanyConfig?> GetByIdAsync(Guid companyId, string key, CancellationToken ct = default) =>
            base.GetByIdAsync(KeyId(companyId, key), ct);

        public override Task<CompanyConfig?> SaveAsync(CompanyConfig model, CancellationToken ct = default) =>
            base.SaveAsync(model with { Id = KeyId(model.CompanyId ?? Guid.Empty, model.Key) }, ct);

        public Task<bool> DeleteAsync(Guid companyId, string key, CancellationToken ct = default) =>
            base.DeleteAsync(KeyId(companyId, key), ct);
    }
}
