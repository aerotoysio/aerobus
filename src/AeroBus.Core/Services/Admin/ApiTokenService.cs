using System.Security.Claims;
using System.Security.Cryptography;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;

namespace AeroBus.Core.Services.Admin
{
    /// <summary>
    /// Encapsulates API-key generation and validation. Key format is
    /// <c>ab_&lt;8-char-prefix&gt;_&lt;43-char-base64url-secret&gt;</c>. The 32-byte
    /// secret is SHA-256 hashed at rest; the plaintext is shown to the operator
    /// exactly once at creation time and is never retrievable thereafter.
    /// </summary>
    public sealed class ApiTokenService(IApiTokens repo)
    {
        private readonly IApiTokens _repo = repo;

        // ab_ + 8 chars + _ + 43 chars (base64url of 32 bytes) = 55 chars total.
        private const string KeyPrefixLiteral = "ab_";
        private const int PrefixLength = 8;
        private const int SecretBytes = 32;

        /// <summary>Identifies the format used by API-key bearer tokens.</summary>
        public static bool LooksLikeApiKey(string? bearer) =>
            !string.IsNullOrEmpty(bearer) && bearer.StartsWith(KeyPrefixLiteral, StringComparison.Ordinal);

        /// <summary>
        /// Generate a new API key, persist its hashed form, and return both the
        /// stored record (without secret) and the plaintext token. The plaintext
        /// is the only opportunity to retain the key — it is not stored.
        /// </summary>
        public async Task<ApiTokenCreated> GenerateAsync(
            Guid companyId,
            string name,
            string? scopes = null,
            DateTime? expires = null,
            CancellationToken ct = default)
        {
            if (companyId == Guid.Empty) throw new ArgumentException("CompanyId required.", nameof(companyId));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));

            var prefix = NewPrefix();
            var secretBytes = RandomNumberGenerator.GetBytes(SecretBytes);
            var hash = SHA256.HashData(secretBytes);
            var plaintext = $"{KeyPrefixLiteral}{prefix}_{Base64Url.Encode(secretBytes)}";

            var saved = await _repo.SaveAsync(new ApiToken
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Name = name.Trim(),
                Prefix = prefix,
                Hash = hash,
                Scopes = string.IsNullOrWhiteSpace(scopes) ? null : scopes.Trim(),
                Expires = expires,
                Created = DateTime.UtcNow,
            }, ct: ct);

            if (saved is null) throw new InvalidOperationException("Failed to persist new API token.");

            return new ApiTokenCreated(saved, plaintext);
        }

        /// <summary>
        /// Validate a presented bearer token. Returns the matching record on
        /// success and updates LastUsed/LastIP. Returns null for any rejection
        /// (bad format, no match, revoked, expired). Constant-time hash compare.
        /// </summary>
        public async Task<ApiToken?> ValidateAsync(
            string bearer,
            string? remoteIp = null,
            CancellationToken ct = default)
        {
            if (!TryParseBearer(bearer, out var prefix, out var secretBytes))
                return null;

            var candidates = await _repo.GetByPrefixAsync(prefix, ct: ct);
            if (candidates.Count == 0) return null;

            var presentedHash = SHA256.HashData(secretBytes);
            var now = DateTime.UtcNow;

            foreach (var record in candidates)
            {
                if (record.Hash is null) continue;
                if (record.Hash.Length != presentedHash.Length) continue;
                if (!CryptographicOperations.FixedTimeEquals(record.Hash, presentedHash)) continue;

                if (record.Revoked is not null) return null;
                if (record.Expires is not null && record.Expires <= now) return null;

                // Fire-and-forget would lose audit data on failure; await it.
                await _repo.TouchLastUsedAsync(record.Id, remoteIp, ct: ct);

                return record;
            }

            return null;
        }

        /// <summary>List a company's tokens — never includes the hash to callers.</summary>
        public async Task<IReadOnlyList<ApiToken>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default)
        {
            var rows = await _repo.GetByCompanyAsync(companyId, ct: ct);
            // Hash field is sensitive — strip before returning to callers.
            return rows.Select(r => new ApiToken
            {
                Id = r.Id,
                CompanyId = r.CompanyId,
                Name = r.Name,
                Prefix = r.Prefix,
                Hash = null,
                Scopes = r.Scopes,
                Expires = r.Expires,
                LastUsed = r.LastUsed,
                LastIP = r.LastIP,
                Created = r.Created,
                Revoked = r.Revoked,
            }).ToList();
        }

        public Task RevokeAsync(Guid id, CancellationToken ct = default) => _repo.RevokeAsync(id, ct: ct);

        /// <summary>
        /// Update editable metadata (name, scopes, expiry) on an existing
        /// token. Hash, prefix, company, created/last-used, and revoked-state
        /// are immutable from this entry-point. Pass null on any field to
        /// leave it unchanged. Returns the updated record with Hash stripped.
        /// </summary>
        public async Task<ApiToken?> UpdateAsync(
            Guid id,
            string? name = null,
            string? scopes = null,
            DateTime? expires = null,
            bool clearExpires = false,
            CancellationToken ct = default)
        {
            var existing = await _repo.GetByIdAsync(id, ct: ct);
            if (existing is null) return null;

            var saved = await _repo.SaveAsync(new ApiToken
            {
                Id = existing.Id,
                CompanyId = existing.CompanyId,
                Name = string.IsNullOrWhiteSpace(name) ? existing.Name : name.Trim(),
                Prefix = existing.Prefix,
                Hash = existing.Hash,
                Scopes = scopes is null
                    ? existing.Scopes
                    : (string.IsNullOrWhiteSpace(scopes) ? null : scopes.Trim()),
                Expires = clearExpires ? null : (expires ?? existing.Expires),
                LastUsed = existing.LastUsed,
                LastIP = existing.LastIP,
                Created = existing.Created,
                Revoked = existing.Revoked,
            }, ct: ct);

            // Strip Hash before returning to callers.
            if (saved is null) return null;
            return new ApiToken
            {
                Id = saved.Id,
                CompanyId = saved.CompanyId,
                Name = saved.Name,
                Prefix = saved.Prefix,
                Hash = null,
                Scopes = saved.Scopes,
                Expires = saved.Expires,
                LastUsed = saved.LastUsed,
                LastIP = saved.LastIP,
                Created = saved.Created,
                Revoked = saved.Revoked,
            };
        }

        /// <summary>
        /// Build a ClaimsPrincipal representing an API-token-authenticated caller.
        /// Mirrors the user-JWT claim shape so existing authorisation policies
        /// (companyId, perm) work without per-handler branching.
        /// </summary>
        public static ClaimsPrincipal BuildPrincipal(ApiToken token, string authenticationScheme)
        {
            var claims = new List<Claim>
            {
                new("sub", $"apitoken:{token.Id}"),
                new("companyId", token.CompanyId?.ToString() ?? Guid.Empty.ToString()),
                new("apitoken", "1"),
                new("apitoken_id", token.Id.ToString()),
                new("apitoken_prefix", token.Prefix ?? string.Empty),
            };

            if (!string.IsNullOrWhiteSpace(token.Scopes))
            {
                foreach (var scope in token.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // Treat scopes as 'perm' claims so the existing PermissionHandler
                    // policy provider applies without modification.
                    claims.Add(new Claim("perm", scope));
                }
            }

            var identity = new ClaimsIdentity(claims, authenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        private static string NewPrefix()
        {
            // 8 chars from a 36-char alphabet — ~41 bits of entropy in the
            // prefix alone, enough to keep collisions on (Prefix, Hash) lookups
            // negligible at expected scale.
            const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            Span<char> buf = stackalloc char[PrefixLength];
            for (int i = 0; i < PrefixLength; i++)
                buf[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            return new string(buf);
        }

        private static bool TryParseBearer(string bearer, out string prefix, out byte[] secret)
        {
            prefix = string.Empty;
            secret = Array.Empty<byte>();

            if (string.IsNullOrEmpty(bearer)) return false;
            if (!bearer.StartsWith(KeyPrefixLiteral, StringComparison.Ordinal)) return false;

            var rest = bearer.AsSpan(KeyPrefixLiteral.Length);
            var underscore = rest.IndexOf('_');
            if (underscore != PrefixLength) return false;

            var prefixSpan = rest[..PrefixLength];
            var secretSpan = rest[(PrefixLength + 1)..];
            if (secretSpan.Length == 0) return false;

            try
            {
                secret = Base64Url.Decode(secretSpan.ToString());
            }
            catch
            {
                return false;
            }
            if (secret.Length != SecretBytes) return false;

            prefix = prefixSpan.ToString();
            return true;
        }
    }

    /// <summary>Result of <see cref="ApiTokenService.GenerateAsync"/>. The
    /// plaintext is shown once to the operator and never persisted.</summary>
    public sealed record ApiTokenCreated(ApiToken Record, string Plaintext);

    internal static class Base64Url
    {
        public static string Encode(ReadOnlySpan<byte> bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string s)
        {
            var padded = s.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
