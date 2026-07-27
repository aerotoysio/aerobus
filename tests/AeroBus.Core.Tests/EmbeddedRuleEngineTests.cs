using System.Text.Json;
using AeroBus.Core.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// The RuleForge engine EMBEDDED: publish the repo's real shop-bundles rule +
/// reference sets into DocumentForge (the same three collections the external
/// engine reads), then evaluate in-process through EmbeddedRuleForgeClient —
/// priced bundles with no RuleForge service anywhere.
/// </summary>
[Collection("documentforge")]
public class EmbeddedRuleEngineTests(DocumentForgeFixture fx)
{
    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private sealed class StaticSettingsScope : IRuleForgeSettingsProvider
    {
        public Task<RuleForgeSettings> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new RuleForgeSettings("embedded", "", 2000, "dev"));
    }

    private static string Rules(string file) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", file);

    private async Task SeedRuleAsync()
    {
        // rules header + version snapshot + env binding + reference sets — the
        // exact documents the DocumentForge-backed sources read.
        var rule = JsonDocument.Parse(File.ReadAllText(Rules("rule-shop-bundles.json"))).RootElement;
        var ruleId = rule.GetProperty("id").GetString()!;

        await fx.Client.InsertAsync("rules", JsonSerializer.Serialize(new
        {
            id = ruleId,
            name = rule.GetProperty("name").GetString(),
            endpoint = rule.GetProperty("endpoint").GetString(),
            method = rule.GetProperty("method").GetString(),
            status = "active",
            currentVersion = 1,
        }));
        await fx.Client.InsertAsync("ruleversions", JsonSerializer.Serialize(new
        {
            id = $"{ruleId}@1",
            ruleId,
            version = 1,
            snapshot = rule,
        }));
        await fx.Client.InsertAsync("environments", JsonSerializer.Serialize(new
        {
            id = "env-dev",
            name = "dev",
            ruleBindings = new Dictionary<string, int> { [ruleId] = 1 },
        }));

        foreach (var file in new[] { "ref-basefares.json", "ref-bundle-markups.json" })
        {
            var set = JsonDocument.Parse(File.ReadAllText(Rules(file))).RootElement;
            var refId = set.GetProperty("id").GetString()!;
            await fx.Client.InsertAsync("referencesets", JsonSerializer.Serialize(new
            {
                id = refId,
                name = set.GetProperty("name").GetString(),
                currentVersion = 1,
            }));
            await fx.Client.InsertAsync("referencesetversions", JsonSerializer.Serialize(new
            {
                id = $"{refId}@1",
                refId,
                version = 1,
                columns = set.GetProperty("columns"),
                rows = set.GetProperty("rows"),
            }));
        }
    }

    [Fact]
    public async Task Embedded_engine_prices_the_real_shop_bundles_rule()
    {
        await SeedRuleAsync();

        var services = new ServiceCollection();
        services.AddScoped<IRuleForgeSettingsProvider, StaticSettingsScope>();
        var provider = services.BuildServiceProvider();

        await using var embedded = new EmbeddedRuleForgeClient(
            new Opt<AeroBus.Core.Data.DocumentForgeOptions>(new AeroBus.Core.Data.DocumentForgeOptions
            {
                BaseUrl = fx.BaseUrl,
                ApiKey = Environment.GetEnvironmentVariable("DOCUMENTFORGE_APIKEY") ?? "",
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmbeddedRuleForgeClient>.Instance);

        Assert.True(await embedded.HealthAsync());

        var paxId = Guid.NewGuid();
        var payload = new
        {
            searchContext = new { currency = "AED", origin = "SYD", destination = "MEL" },
            flightSolution = new
            {
                id = Guid.NewGuid(),
                origin = "SYD",
                destination = "MEL",
                cabin = "Y",
                elapsedDurationMinutes = 95,
                legs = new[] { new { flightRef = Guid.NewGuid().ToString(), marketingCarrier = "SM", from = "SYD", to = "MEL" } },
            },
            paxIds = new[] { paxId },
            bundles = new[]
            {
                new { id = Guid.NewGuid(), code = "LITE", name = "Lite", description = (string?)null,
                      category = (string?)null, products = Array.Empty<object>() },
                new { id = Guid.NewGuid(), code = "FLEX", name = "Flex", description = (string?)null,
                      category = (string?)null, products = Array.Empty<object>() },
            },
            products = Array.Empty<object>(),
        };

        var envelope = await embedded.EvaluateAsync("/v1/offer/shop-bundles", payload);

        Assert.Equal(Decision.Apply, envelope.Decision);
        Assert.Equal("rule-shop-bundles", envelope.RuleId);
        Assert.NotNull(envelope.Result);

        // The rule prices from ref-basefares (SYD-MEL base 180) x markup + 10% tax.
        var result = envelope.Result!.Value;
        var bundles = result.ValueKind == JsonValueKind.Array
            ? result
            : result.GetProperty("bundles");
        Assert.True(bundles.GetArrayLength() >= 2);
        var first = bundles[0];
        var total = first.TryGetProperty("total", out var t)
            ? t.GetDecimal()
            : first.GetProperty("price").GetProperty("total").GetDecimal();
        Assert.True(total > 0, $"expected a priced bundle, got total={total}");
    }
}
