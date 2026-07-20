using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IOrganisations
    {
        Task<Organisation?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Organisation?> GetByShortNameAsync(string shortName, CancellationToken ct = default);
        Task<IReadOnlyList<Organisation>> GetAllAsync(CancellationToken ct = default);
        Task<Organisation?> SaveAsync(Organisation model, CancellationToken ct = default);
    }

    /// <summary>
    /// The tenant registry, in the <c>organisations</c> collection of the fixed
    /// CONTROL database (the keyed control store — never the tenant-routed store,
    /// since this is what drives the routing). One row per airline: org id → its
    /// DocumentForge database name.
    /// </summary>
    public sealed class Organisations : DocumentRepository<Organisation>, IOrganisations
    {
        public Organisations(
            [FromKeyedServices(Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentStore controlStore)
            : base(controlStore) { }

        protected override string Collection => DfCollections.Admin.Organisations;

        public Task<Organisation?> GetByShortNameAsync(string shortName, CancellationToken ct = default) =>
            GetByFieldAsync(Df.Field(nameof(Organisation.ShortName)), shortName, ct);

        public Task<IReadOnlyList<Organisation>> GetAllAsync(CancellationToken ct = default) =>
            ListAsync(ct: ct);
    }
}
