using System.Collections.Concurrent;
using AeroBus.Core.Data;
using AeroBus.Core.Events;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Admin
{
    /// <summary>What the admin API returns: secrets are flagged and masked, never echoed.</summary>
    public sealed record PlatformConfigView(
        string Key, string? Value, bool IsSecret, string? Description, DateTime? Updated);

    /// <summary>
    /// Platform settings live in the control DATABASE, not in appsettings — the
    /// only bootstrap settings left in configuration files are how to reach
    /// Keycloak and DocumentForge. This service is the one read/write path:
    ///
    /// <list type="bullet">
    /// <item>Secrets are encrypted at rest with Data Protection and are
    /// WRITE-ONLY through the admin API — reads return a mask.</item>
    /// <item>Values are cached process-wide with a short TTL so hot paths (the
    /// RuleForge client resolves its settings per call) don't hit the store;
    /// a write invalidates immediately in this process, other instances catch
    /// up within the TTL.</item>
    /// <item>Every change emits a <c>platform.config-changed</c> event (the
    /// audit trail comes free on the event backbone; secret values are never
    /// in the payload).</item>
    /// </list>
    /// </summary>
    public sealed class PlatformConfigService
    {
        public const string SecretMask = "••••••••";
        private const string ProtectorPurpose = "AeroBus.PlatformConfig.v1";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        // Process-wide cache: key → (decrypted value or null-if-absent, expiry).
        private static readonly ConcurrentDictionary<string, (string? Value, DateTime ExpiresUtc)> Cache = new();

        private readonly IPlatformConfigs _repo;
        private readonly IDataProtector _protector;
        private readonly IEventPublisher _events;
        private readonly ILogger<PlatformConfigService> _log;

        public PlatformConfigService(
            IPlatformConfigs repo,
            IDataProtectionProvider dataProtection,
            IEventPublisher events,
            ILogger<PlatformConfigService> log)
        {
            _repo = repo;
            _protector = dataProtection.CreateProtector(ProtectorPurpose);
            _events = events;
            _log = log;
        }

        /// <summary>Effective (decrypted) value of a key, or null when unset. Cached.</summary>
        public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        {
            var k = PlatformConfigs.Normalize(key);
            if (Cache.TryGetValue(k, out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
                return hit.Value;

            var row = await _repo.GetByKeyAsync(k, ct);
            var value = Decrypt(row);
            Cache[k] = (value, DateTime.UtcNow.Add(CacheTtl));
            return value;
        }

        /// <summary>Effective value with a fallback for when the key is unset (the appsettings bootstrap value).</summary>
        public async Task<string> GetOrDefaultAsync(string key, string fallback, CancellationToken ct = default) =>
            await GetAsync(key, ct) ?? fallback;

        /// <summary>All entries for the admin UI — secret values masked, never echoed.</summary>
        public async Task<IReadOnlyList<PlatformConfigView>> ListAsync(CancellationToken ct = default)
        {
            var rows = await _repo.GetAllAsync(ct);
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => new PlatformConfigView(
                    r.Key!, r.IsSecret ? SecretMask : r.Value, r.IsSecret, r.Description, r.Updated))
                .ToList();
        }

        /// <summary>
        /// Create or replace a key. Secrets are encrypted before they touch the
        /// store. Emits <c>platform.config-changed</c> (no secret values in the
        /// event payload).
        /// </summary>
        public async Task<PlatformConfigView> SetAsync(
            string key, string? value, bool isSecret, string? description, string? actor, CancellationToken ct = default)
        {
            var k = PlatformConfigs.Normalize(key);
            var stored = isSecret && !string.IsNullOrEmpty(value) ? _protector.Protect(value) : value;

            var saved = await _repo.SaveAsync(new PlatformConfig
            {
                Key = k,
                Value = stored,
                IsSecret = isSecret,
                Description = description,
            }, ct) ?? throw new InvalidOperationException($"Saving platform config '{k}' returned no document.");

            Cache[k] = (isSecret ? value : saved.Value, DateTime.UtcNow.Add(CacheTtl));

            await _events.PublishAsync(
                "platform.config-changed",
                new EventSubject(DfCollections.Admin.PlatformConfig, k),
                new { key = k, isSecret, description },
                companyId: null, actor: actor ?? "platform-admin", ct);

            _log.LogInformation("Platform config '{Key}' updated ({Kind}).", k, isSecret ? "secret" : "plain");
            return new PlatformConfigView(k, isSecret ? SecretMask : saved.Value, isSecret, saved.Description, saved.Updated);
        }

        public async Task<bool> DeleteAsync(string key, string? actor, CancellationToken ct = default)
        {
            var k = PlatformConfigs.Normalize(key);
            var removed = await _repo.DeleteByKeyAsync(k, ct);
            Cache.TryRemove(k, out _);
            if (removed)
                await _events.PublishAsync(
                    "platform.config-changed",
                    new EventSubject(DfCollections.Admin.PlatformConfig, k),
                    new { key = k, deleted = true },
                    companyId: null, actor: actor ?? "platform-admin", ct);
            return removed;
        }

        /// <summary>
        /// One-time migration path run at startup: if a key is unset in the
        /// database but the appsettings bootstrap has a value, move it in (so a
        /// fresh install configured the old way converges on database-held
        /// settings without hand-holding). Never overwrites an existing row.
        /// </summary>
        public async Task SeedIfMissingAsync(
            string key, string? value, bool isSecret, string? description, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var k = PlatformConfigs.Normalize(key);
            if (await _repo.GetByKeyAsync(k, ct) is not null) return;
            await SetAsync(k, value, isSecret, description, actor: "startup-seed", ct);
        }

        private string? Decrypt(PlatformConfig? row)
        {
            if (row?.Value is null) return row?.Value;
            if (!row.IsSecret) return row.Value;
            try
            {
                return _protector.Unprotect(row.Value);
            }
            catch (Exception ex)
            {
                // A key-ring change makes old secrets unreadable; treat as unset
                // (callers fall back to bootstrap config) and say so loudly.
                _log.LogError(ex,
                    "Platform config secret '{Key}' could not be decrypted (Data Protection key ring changed?); treating as unset.",
                    row.Key);
                return null;
            }
        }
    }
}
