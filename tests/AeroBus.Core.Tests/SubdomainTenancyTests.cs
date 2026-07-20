using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;
using AeroBus.Core.Security;
using AeroBus.Core.Services.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Host-based tenant resolution: a subdomain resolves the org for anonymous
/// requests, the token stays authoritative for authenticated ones, and a
/// token/subdomain mismatch is rejected outright (a Host header is
/// attacker-controlled and must never re-scope an authenticated call).
/// </summary>
public class SubdomainTenancyTests
{
    private static readonly Guid VerifyOrg = Guid.Parse("4329f587-6499-43ba-a3ea-d9f4d7ab80bb");

    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private sealed class FakeTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CallerCompanyId => companyId;
        public bool BypassTenancy { get; set; }
        public bool IsApiToken => false;
    }

    private sealed class FakeOrgs : IOrganisations
    {
        private readonly Dictionary<string, Organisation> _byShort = new(StringComparer.OrdinalIgnoreCase);
        public FakeOrgs Add(Organisation o) { _byShort[o.ShortName!] = o; return this; }
        public Task<Organisation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_byShort.Values.FirstOrDefault(o => o.Id == id));
        public Task<Organisation?> GetByShortNameAsync(string shortName, CancellationToken ct = default) =>
            Task.FromResult(_byShort.TryGetValue(shortName, out var o) ? o : null);
        public Task<IReadOnlyList<Organisation>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Organisation>>(_byShort.Values.ToList());
        public Task<Organisation?> SaveAsync(Organisation model, CancellationToken ct = default) =>
            Task.FromResult<Organisation?>(model);
    }

    private sealed class EmptyPlatformConfigs : IPlatformConfigs
    {
        public Task<PlatformConfig?> GetByKeyAsync(string key, CancellationToken ct = default) => Task.FromResult<PlatformConfig?>(null);
        public Task<IReadOnlyList<PlatformConfig>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PlatformConfig>>([]);
        public Task<PlatformConfig?> SaveAsync(PlatformConfig model, CancellationToken ct = default) => Task.FromResult<PlatformConfig?>(model);
        public Task<bool> DeleteByKeyAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class NullEvents : Core.Events.IEventPublisher
    {
        public Task<Core.Events.OutboxEvent?> PublishAsync(string type, Core.Events.EventSubject subject, object data, Guid? companyId, string? actor = null, CancellationToken ct = default) =>
            Task.FromResult<Core.Events.OutboxEvent?>(null);
    }

    private static async Task<(TenantDatabase Db, HttpContext Ctx, bool NextRan)> RunAsync(
        string host, Guid? tokenOrg, string? baseDomain)
    {
        var nextRan = false;
        var middleware = new TenantDatabaseMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        var db = new TenantDatabase(new Opt<DocumentForgeOptions>(new DocumentForgeOptions()));
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        ctx.Response.Body = new MemoryStream();

        var orgs = new FakeOrgs().Add(new Organisation
        {
            Id = VerifyOrg, ShortName = "verify", Name = "Verify Air", Status = "Active",
        });

        var platformConfig = new PlatformConfigService(
            new EmptyPlatformConfigs(), new EphemeralDataProtectionProvider(), new NullEvents(),
            NullLogger<PlatformConfigService>.Instance);

        await middleware.InvokeAsync(
            ctx, new FakeTenant(tokenOrg), db, orgs, new MemoryCache(new MemoryCacheOptions()),
            platformConfig, new Opt<TenancyOptions>(new TenancyOptions { BaseDomain = baseDomain }),
            NullLogger<TenantDatabaseMiddleware>.Instance);

        return (db, ctx, nextRan);
    }

    [Fact]
    public async Task Anonymous_request_on_tenant_subdomain_resolves_the_org_database()
    {
        var (db, _, nextRan) = await RunAsync("verify.aerotoys.io", tokenOrg: null, baseDomain: "aerotoys.io");
        Assert.True(nextRan);
        Assert.True(db.IsTenantResolved);
        Assert.Equal("verify", db.CurrentDatabase);
    }

    [Fact]
    public async Task Unknown_subdomain_and_www_and_base_domain_stamp_nothing()
    {
        foreach (var host in new[] { "nosuchorg.aerotoys.io", "www.aerotoys.io", "aerotoys.io", "deep.verify.aerotoys.io" })
        {
            var (db, _, nextRan) = await RunAsync(host, tokenOrg: null, baseDomain: "aerotoys.io");
            Assert.True(nextRan);
            Assert.False(db.IsTenantResolved);
        }
    }

    [Fact]
    public async Task No_base_domain_disables_host_resolution()
    {
        var (db, _, nextRan) = await RunAsync("verify.aerotoys.io", tokenOrg: null, baseDomain: null);
        Assert.True(nextRan);
        Assert.False(db.IsTenantResolved);
    }

    [Fact]
    public async Task Token_resolves_the_org_and_matching_subdomain_is_fine()
    {
        var (db, _, nextRan) = await RunAsync("verify.aerotoys.io", tokenOrg: VerifyOrg, baseDomain: "aerotoys.io");
        Assert.True(nextRan);
        Assert.Equal("verify", db.CurrentDatabase);
    }

    [Fact]
    public async Task Token_and_subdomain_mismatch_is_rejected()
    {
        var (db, ctx, nextRan) = await RunAsync("otherair.aerotoys.io", tokenOrg: VerifyOrg, baseDomain: "aerotoys.io");
        Assert.False(nextRan);
        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        Assert.False(db.IsTenantResolved);
    }
}
