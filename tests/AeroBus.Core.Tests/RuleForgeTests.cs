using System.Net;
using System.Text;
using System.Text.Json;
using AeroBus.Core.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Unit tests for the RuleForge client envelope parsing and the DecisionRunner
/// failure modes. No live RuleForge — a fake <see cref="HttpMessageHandler"/> is
/// injected into the typed client, so these run offline alongside the live suite.
/// </summary>
public class RuleForgeTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class Opt<T>(T v) : IOptions<T> where T : class { public T Value => v; }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static RuleForgeClient ClientFor(StubHandler handler, RuleForgeOptions? options = null)
    {
        var o = options ?? new RuleForgeOptions { BaseUrl = "http://localhost:5050" };
        return new(new HttpClient(handler),
            new StaticRuleForgeSettingsProvider(new RuleForgeSettings(o.BaseUrl, o.ApiKey, o.TimeoutMs)));
    }

    private static DecisionRunner RunnerFor(IRuleForgeClient client, RuleForgeOptions? options = null, DecisionRunnerOptions? runnerOptions = null) =>
        new(client,
            new Opt<RuleForgeOptions>(options ?? new RuleForgeOptions { BaseUrl = "http://localhost:5050" }),
            new Opt<DecisionRunnerOptions>(runnerOptions ?? new DecisionRunnerOptions()),
            NullLogger<DecisionRunner>.Instance);

    // ─── envelope parsing ─────────────────────────────────────────────────────

    [Fact]
    public async Task Parses_apply_envelope_with_result_and_lowercase_decision()
    {
        const string body = """
        {
          "ruleId": "rule-shop-bundles",
          "ruleVersion": 3,
          "decision": "apply",
          "evaluatedAt": "2026-07-08T10:00:00.000Z",
          "result": { "bundles": [ { "code": "LITE" } ] },
          "durationMs": 4
        }
        """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        var client = ClientFor(handler);

        var env = await client.EvaluateAsync("/v1/offer/shop-bundles", new { x = 1 });

        Assert.Equal("rule-shop-bundles", env.RuleId);
        Assert.Equal(3, env.RuleVersion);
        Assert.Equal(Decision.Apply, env.Decision);
        Assert.Equal(4, env.DurationMs);
        Assert.NotNull(env.Result);
        Assert.True(env.Result!.Value.TryGetProperty("bundles", out var bundles));
        Assert.Equal(JsonValueKind.Array, bundles.ValueKind);
    }

    [Theory]
    [InlineData("skip", Decision.Skip)]
    [InlineData("error", Decision.Error)]
    public async Task Parses_skip_and_error_decisions(string raw, Decision expected)
    {
        var body = $$"""{ "ruleId": "r", "ruleVersion": 1, "decision": "{{raw}}", "evaluatedAt": "t", "result": null }""";
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.OK, body)));

        var env = await client.EvaluateAsync("/v1/offer/shop-bundles", new { });

        Assert.Equal(expected, env.Decision);
    }

    [Fact]
    public async Task Sends_api_key_header_and_debug_query_when_requested()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, EnvelopeJson()));
        var options = new RuleForgeOptions { BaseUrl = "http://localhost:5050", ApiKey = "secret-key" };
        var client = ClientFor(handler, options);

        _ = await client.EvaluateAsync("/v1/offer/shop-bundles", new { }, debug: true);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues(RuleForgeClient.ApiKeyHeader, out var vals));
        Assert.Equal("secret-key", vals!.Single());
        Assert.Contains("debug=true", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task Non_success_http_throws()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.InternalServerError, "boom")));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.EvaluateAsync("/v1/offer/shop-bundles", new { }));
    }

    [Fact]
    public async Task Refresh_returns_true_on_success()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.OK, """{ "ok": true }""")));
        Assert.True(await client.RefreshAsync());
    }

    // ─── decision runner failure modes ────────────────────────────────────────

    [Fact]
    public async Task ShopBundles_applies_when_rule_applies()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.OK, EnvelopeJson(decision: "apply"))));
        var runner = RunnerFor(client);

        var outcome = await runner.RunAsync(DecisionPoint.ShopBundles, new { });

        Assert.True(outcome.Applied);
        Assert.False(outcome.Degraded);
        Assert.Null(outcome.Warning);
    }

    [Fact]
    public async Task ShopBundles_degrades_on_http_error()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "down")));
        var runner = RunnerFor(client);

        var outcome = await runner.RunAsync(DecisionPoint.ShopBundles, new { });

        Assert.False(outcome.Applied);
        Assert.True(outcome.Degraded);
        Assert.NotNull(outcome.Warning);
    }

    [Fact]
    public async Task ShopBundles_degrades_on_decision_error()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.OK, EnvelopeJson(decision: "error"))));
        var runner = RunnerFor(client);

        var outcome = await runner.RunAsync(DecisionPoint.ShopBundles, new { });

        Assert.True(outcome.Degraded);
        Assert.NotNull(outcome.Warning);
    }

    [Fact]
    public async Task Skip_is_not_degraded_but_not_applied()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.OK, EnvelopeJson(decision: "skip"))));
        var runner = RunnerFor(client);

        var outcome = await runner.RunAsync(DecisionPoint.ShopBundles, new { });

        Assert.False(outcome.Applied);
        Assert.False(outcome.Degraded);
    }

    [Fact]
    public void Order_points_default_to_allow_shop_defaults_to_degrade()
    {
        var runner = RunnerFor(ClientFor(new StubHandler(_ => Json(HttpStatusCode.OK, EnvelopeJson()))));

        Assert.Equal(FailureMode.Degrade, runner.FailureModeFor(DecisionPoint.ShopBundles));
        Assert.Equal(FailureMode.Allow, runner.FailureModeFor(DecisionPoint.OrderValidate));
        Assert.Equal(FailureMode.Allow, runner.FailureModeFor(DecisionPoint.RefundEligibility));
    }

    [Fact]
    public async Task Config_override_changes_failure_mode()
    {
        var client = ClientFor(new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "down")));
        var runnerOptions = new DecisionRunnerOptions
        {
            FailureModes = new() { ["OrderValidate"] = FailureMode.Deny },
        };
        var runner = RunnerFor(client, runnerOptions: runnerOptions);

        Assert.Equal(FailureMode.Deny, runner.FailureModeFor(DecisionPoint.OrderValidate));
        var outcome = await runner.RunAsync(DecisionPoint.OrderValidate, new { });
        Assert.True(outcome.Degraded);
        Assert.Contains("denied", outcome.Warning);
    }

    private static string EnvelopeJson(string decision = "apply") => $$"""
    {
      "ruleId": "rule-x",
      "ruleVersion": 1,
      "decision": "{{decision}}",
      "evaluatedAt": "2026-07-08T10:00:00.000Z",
      "result": { "bundles": [] }
    }
    """;
}
