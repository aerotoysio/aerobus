using System.Text.Json;

namespace AeroBus.Core.Data.Postgres
{
    /// <summary>
    /// The storage serializer for <c>doc jsonb</c> columns — identical policy to
    /// DocumentForge storage (camelCase fields, case-insensitive reads), so the
    /// aggregate JSON is byte-for-byte the same in either store and the wire
    /// never notices which one served it.
    /// </summary>
    public static class PgJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static string Serialize<T>(T model) => JsonSerializer.Serialize(model, Options);
        public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
