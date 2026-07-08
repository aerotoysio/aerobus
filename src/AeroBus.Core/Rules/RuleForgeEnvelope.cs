using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroBus.Core.Rules
{
    /// <summary>
    /// The response envelope every RuleForge rule emits. Mirrors the engine's
    /// contract: <c>{ruleId, ruleVersion, decision, evaluatedAt, result, trace?, durationMs}</c>.
    /// <see cref="Result"/> and <see cref="Trace"/> are left as raw
    /// <see cref="JsonElement"/> — the shape is rule-specific and AeroBus maps it
    /// per decision point.
    /// </summary>
    public sealed record RuleForgeEnvelope(
        [property: JsonPropertyName("ruleId")] string RuleId,
        [property: JsonPropertyName("ruleVersion")] int RuleVersion,
        [property: JsonPropertyName("decision")] Decision Decision,
        [property: JsonPropertyName("evaluatedAt")] string EvaluatedAt,
        [property: JsonPropertyName("result")] JsonElement? Result,
        [property: JsonPropertyName("trace")] JsonElement? Trace = null,
        [property: JsonPropertyName("durationMs")] long? DurationMs = null);

    /// <summary>The rule's verdict for a request. Serialized as lowercase to
    /// match the engine (<c>apply</c> / <c>skip</c> / <c>error</c>).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Decision>))]
    public enum Decision
    {
        [JsonStringEnumMemberName("apply")] Apply,
        [JsonStringEnumMemberName("skip")] Skip,
        [JsonStringEnumMemberName("error")] Error,
    }
}
