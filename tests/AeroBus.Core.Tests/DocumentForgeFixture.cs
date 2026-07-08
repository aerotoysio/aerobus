using AeroBus.Core.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// One-time DocumentForge connectivity gate shared by the whole test assembly.
/// If DF is unreachable the constructor throws, so the run goes RED with an
/// actionable message instead of silently passing (a per-test
/// "if (!reachable) return;" would let a down database masquerade as success).
///
/// Exposes a shared store + client. Tests create random ids under a fresh
/// company and delete what they create, so runs are repeatable and isolated.
/// </summary>
public sealed class DocumentForgeFixture
{
    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    public IDocumentForgeClient Client { get; }
    public IDocumentStore Store { get; }
    public string BaseUrl { get; }

    public DocumentForgeFixture()
    {
        BaseUrl = Environment.GetEnvironmentVariable("DOCUMENTFORGE_BASEURL") ?? "http://localhost:4300";
        Client = new DocumentForgeClient(new HttpClient(), new Opt<DocumentForgeOptions>(new DocumentForgeOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = Environment.GetEnvironmentVariable("DOCUMENTFORGE_APIKEY") ?? "",
        }));

        var health = Client.HealthAsync().GetAwaiter().GetResult();
        if (!health.Reachable)
            throw new InvalidOperationException(
                $"DocumentForge is not reachable at {BaseUrl} " +
                $"(status {health.StatusCode}{(string.IsNullOrEmpty(health.Error) ? "" : $", {health.Error}")}). " +
                "These are live round-trip tests — start DocumentForge first " +
                "(dfdb serve --port 4300 --data-dir <dir>), or set DOCUMENTFORGE_BASEURL to point elsewhere.");

        Store = new DocumentStore(Client);
    }

    /// <summary>A fresh tenant id so each test's data is naturally isolated.</summary>
    public static Guid NewCompany() => Guid.NewGuid();
}

[CollectionDefinition("documentforge")]
public sealed class DocumentForgeCollection : ICollectionFixture<DocumentForgeFixture> { }
