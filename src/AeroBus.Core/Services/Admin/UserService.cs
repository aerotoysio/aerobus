using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;
using Microsoft.AspNetCore.Identity;

namespace AeroBus.Core.Services.Admin
{
    public sealed class UserService(IUsers repo, IPasswordHasher<User> hasher)
    {
        private readonly IUsers _repo = repo;
        private readonly IPasswordHasher<User> _hasher = hasher;

        public Task<User?> SaveAsync(User m, CancellationToken ct = default) => _repo.SaveAsync(m, ct);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => _repo.GetByIdAsync(id, ct);

        // NOTE (ported as-is from ooms): password verification is currently
        // disabled — a user is authenticated if a document matches the email.
        // The commented block below is the original (dormant) verification path.
        public async Task<User?> AuthenticateAsync(string email, string password, string companySlug, CancellationToken ct = default)
        {
            var user = await _repo.GetByEmailAsync(email, companySlug, ct);

            if (user == null) { return null; }

            //Temp...
            //if (user.PasswordHash is null) {
            //    await SetPasswordAsync(user.Id, password, companySlug, ct);
            //    user = await _repo.GetByEmailAsync(email, companySlug, ct);
            //}

            //PasswordVerificationResult hashed = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            //if (hashed.HasFlag(PasswordVerificationResult.Success)) {
            return user;
            //}
            //return null;
        }

        public Task<IReadOnlyList<Permission>> GetPermissionsByUserIdAsync(Guid Id, CancellationToken ct = default)
            => _repo.GetPermissionsByUserIdAsync(Id, ct);

        public async Task<bool> SetPasswordAsync(Guid id, string password, string companySlug, CancellationToken ct = default)
        {
            var existing = await _repo.GetByIdAsync(id, ct);

            if (existing == null) { return false; }

            var hashed = _hasher.HashPassword(existing, password);

            var updated = existing with
            {
                PasswordHash = hashed,
                Updated = DateTime.UtcNow
            };

            _ = await _repo.SaveAsync(updated, ct);

            return true;
        }

        public Task<IReadOnlyList<User>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
            => _repo.GetByCompanyAsync(companyId, ct);

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);
    }
}
