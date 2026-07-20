using AeroBus.Core.Common;
using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Admin
{
    public interface IPlatformConfigs
    {
        Task<PlatformConfig?> GetByKeyAsync(string key, CancellationToken ct = default);
        Task<IReadOnlyList<PlatformConfig>> GetAllAsync(CancellationToken ct = default);
        Task<PlatformConfig?> SaveAsync(PlatformConfig model, CancellationToken ct = default);
        Task<bool> DeleteByKeyAsync(string key, CancellationToken ct = default);
    }

    /// <summary>
    /// Platform settings registry (<c>admin.platformconfig</c>) in the fixed
    /// CONTROL database — platform-wide by definition, so never tenant-routed.
    /// The document Id is a deterministic surrogate of the (normalized) key, so
    /// saving the same key always replaces in place.
    /// </summary>
    public sealed class PlatformConfigs : DocumentRepository<PlatformConfig>, IPlatformConfigs
    {
        public PlatformConfigs(
            [FromKeyedServices(Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentStore controlStore)
            : base(controlStore) { }

        protected override string Collection => DfCollections.Admin.PlatformConfig;

        public static string Normalize(string key) => key.Trim().ToLowerInvariant();

        public static Guid IdFor(string key) => DeterministicGuid.FromString($"platformconfig:{Normalize(key)}");

        public Task<PlatformConfig?> GetByKeyAsync(string key, CancellationToken ct = default) =>
            GetByIdAsync(IdFor(key), ct);

        public Task<IReadOnlyList<PlatformConfig>> GetAllAsync(CancellationToken ct = default) =>
            ListAsync(ct: ct);

        public override Task<PlatformConfig?> SaveAsync(PlatformConfig model, CancellationToken ct = default)
        {
            var key = Normalize(model.Key ?? throw new ArgumentException("PlatformConfig.Key is required."));
            var canonical = model with { Id = IdFor(key), Key = key };
            return Store.UpsertAsync(Collection, canonical, canonical.Id, ct);
        }

        public Task<bool> DeleteByKeyAsync(string key, CancellationToken ct = default) =>
            DeleteAsync(IdFor(key), ct);
    }
}
