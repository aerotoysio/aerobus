using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Admin
{
    /// <summary>
    /// Repository for <c>apitokens</c>. Hashes are stored at rest; the
    /// plaintext secret is never persisted. Validate API keys by prefix lookup
    /// then hash-compare in the service layer (see <see cref="Services.Admin.ApiTokenService"/>).
    /// </summary>
    public interface IApiTokens
    {
        Task<ApiToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<ApiToken>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);

        /// <summary>
        /// Returns 0..N tokens matching the supplied prefix. The caller is
        /// responsible for hash-comparing the candidate secret against each row.
        /// </summary>
        Task<IReadOnlyList<ApiToken>> GetByPrefixAsync(string prefix, CancellationToken ct = default);

        Task<ApiToken?> SaveAsync(ApiToken model, CancellationToken ct = default);

        /// <summary>
        /// Hot-path single-column update bumping LastUsed/LastIP after a
        /// successful auth. No-op if the token id no longer exists.
        /// </summary>
        Task TouchLastUsedAsync(Guid id, string? remoteIp, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete: stamps Revoked = UTC now. Idempotent — calling on an
        /// already-revoked row is a no-op.
        /// </summary>
        Task RevokeAsync(Guid id, CancellationToken ct = default);

        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public sealed class ApiTokens(
        [FromKeyedServices(AeroBus.Core.Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentStore store)
        : DocumentRepository<ApiToken>(store), IApiTokens
    {
        protected override string Collection => DfCollections.Admin.ApiTokens;

        public Task<IReadOnlyList<ApiToken>> GetByPrefixAsync(string prefix, CancellationToken ct = default) =>
            QueryAsync(Eq("Prefix", prefix), ct: ct);

        // No partial-update primitive on IDocumentStore: read-modify-write the
        // whole document.
        public async Task TouchLastUsedAsync(Guid id, string? remoteIp, CancellationToken ct = default)
        {
            var existing = await GetByIdAsync(id, ct);
            if (existing is null) return; // no-op if the token no longer exists

            await SaveAsync(new ApiToken
            {
                Id = existing.Id,
                CompanyId = existing.CompanyId,
                Name = existing.Name,
                Prefix = existing.Prefix,
                Hash = existing.Hash,
                Scopes = existing.Scopes,
                Expires = existing.Expires,
                LastUsed = DateTime.UtcNow,
                LastIP = remoteIp,
                Created = existing.Created,
                Revoked = existing.Revoked,
            }, ct);
        }

        // No soft-delete primitive on IDocumentStore: read-modify-write to stamp
        // Revoked. Idempotent.
        public async Task RevokeAsync(Guid id, CancellationToken ct = default)
        {
            var existing = await GetByIdAsync(id, ct);
            if (existing is null || existing.Revoked is not null) return;

            await SaveAsync(new ApiToken
            {
                Id = existing.Id,
                CompanyId = existing.CompanyId,
                Name = existing.Name,
                Prefix = existing.Prefix,
                Hash = existing.Hash,
                Scopes = existing.Scopes,
                Expires = existing.Expires,
                LastUsed = existing.LastUsed,
                LastIP = existing.LastIP,
                Created = existing.Created,
                Revoked = DateTime.UtcNow,
            }, ct);
        }
    }
}
