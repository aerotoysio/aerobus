using System.Text.RegularExpressions;
using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Model.Identity;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Services.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Services.Admin
{
    /// <summary>
    /// SaaS tenant provisioning: turns onboarding into a fully-usable airline.
    /// Keycloak owns the organisation + users; DocumentForge gives each org its OWN
    /// database (named by its short code), created and seeded here so the org can
    /// transact immediately.
    ///
    /// Steps: create the Keycloak org + admin (via <see cref="IdentityService"/>) →
    /// create <c>db/{shortName}</c> → seed it (the <see cref="Company"/> settings doc
    /// + the reference starter pack) → register the org in the control-plane
    /// registry (the routing source of truth). Seeding writes through an explicit
    /// per-database store because the org isn't in the registry yet, so the
    /// auto-routed client can't reach the new database.
    /// </summary>
    public sealed class ProvisioningService
    {
        private static readonly Regex ShortNamePattern = new("^[a-z][a-z0-9]{1,29}$", RegexOptions.Compiled);

        private readonly IdentityService _identity;
        private readonly IOrganisations _organisations;
        private readonly IDocumentStoreFactory _storeFactory;
        private readonly IDocumentForgeClient _control;
        private readonly ILogger<ProvisioningService> _log;

        public ProvisioningService(
            IdentityService identity,
            IOrganisations organisations,
            IDocumentStoreFactory storeFactory,
            [FromKeyedServices(AeroBus.Core.Data.ServiceCollectionExtensions.ControlClientKey)] IDocumentForgeClient control,
            ILogger<ProvisioningService> log)
        {
            _identity = identity;
            _organisations = organisations;
            _storeFactory = storeFactory;
            _control = control;
            _log = log;
        }

        public async Task<OnboardResult> ProvisionAsync(OnboardRequest req, CancellationToken ct = default)
        {
            var shortName = NormalizeShortName(req.ShortName ?? req.Designator ?? req.OrganizationName);
            if (!ShortNamePattern.IsMatch(shortName))
                throw new IdentityException(400, "Short name must be 2–30 chars, start with a letter, lowercase letters/digits only.");
            if (await _organisations.GetByShortNameAsync(shortName, ct) is not null)
                throw new IdentityException(409, $"A tenant database named '{shortName}' already exists.");

            // 1. Keycloak org + admin user (validates name/email/password, 409 on dupes).
            var onboard = await _identity.OnboardAsync(req, ct);
            var companyId = Guid.Parse(onboard.Organization.Id);
            var alias = onboard.Organization.Alias;
            var now = DateTime.UtcNow;

            var designator = (req.Designator ?? shortName).ToUpperInvariant();
            if (designator.Length > 3) designator = designator[..3];
            var currency = string.IsNullOrWhiteSpace(req.OperatingCurrency) ? "USD" : req.OperatingCurrency!.ToUpperInvariant();

            // 2. Create the org's own DocumentForge database.
            if (!await _control.EnsureDatabaseAsync(shortName, ct))
                throw new IdentityException(500, $"Could not create the DocumentForge database '{shortName}'.");

            // 3. Seed the new database (explicit-db store — the org isn't routable yet).
            var store = _storeFactory.ForDatabase(shortName);
            await store.UpsertAsync("companies", new Company
            {
                Id = companyId,
                Name = req.OrganizationName.Trim(),
                Slug = alias,
                Status = "Active",
                Designator = designator,
                AccountingCode = req.AccountingCode,
                OperatingCurrency = currency,
                DefaultExpectedLoadFactor = 0.80m,
                Created = now,
            }, companyId, ct);

            var tzConfigId = Guid.NewGuid();
            await store.UpsertAsync("companyconfigs", new CompanyConfig
            {
                Id = tzConfigId,
                CompanyId = companyId,
                Key = "timezone",
                Value = string.IsNullOrWhiteSpace(req.Timezone) ? "UTC" : req.Timezone,
                Description = "Default operating timezone.",
            }, tzConfigId, ct);

            await ReferenceSeed.SeedAsync(store, companyId, ct);

            // 4. Register in the control-plane registry — the routing source of truth.
            await _organisations.SaveAsync(new Organisation
            {
                Id = companyId,
                OrgAlias = alias,
                ShortName = shortName,
                Name = req.OrganizationName.Trim(),
                Designator = designator,
                AccountingCode = req.AccountingCode,
                OperatingCurrency = currency,
                Timezone = string.IsNullOrWhiteSpace(req.Timezone) ? "UTC" : req.Timezone,
                Status = "Active",
                Plan = req.Plan,
                Created = now,
            }, ct);

            _log.LogInformation("Provisioned organisation {Alias} ({CompanyId}) into database '{ShortName}'.", alias, companyId, shortName);
            return onboard with { Database = shortName };
        }

        private static string NormalizeShortName(string raw)
        {
            var lowered = new string(raw.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return lowered.Length > 30 ? lowered[..30] : lowered;
        }
    }
}
