using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Common.Cache
{
    public interface IAirportResolver
    {
        Airport? Get(string code);
    }

    /// <summary>
    /// Lazy airport lookup: serves from the hot cache when a snapshot is loaded,
    /// otherwise resolves the code against the store on first request and caches
    /// the hit. (The ooms version required a boot-time per-company preload; the
    /// AeroBus version deliberately does not.)
    /// </summary>
    public sealed class CachedAirportResolver(IHotCache cache, IDocumentStore store) : IAirportResolver
    {
        private static string KeyFor(string code) => $"airports.byCode.{code.ToUpperInvariant()}";

        public Airport? Get(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            // 1) whole-list snapshot, if something preloaded it
            if (cache.TryGet<Airport>(CacheKeys.Airports, out var rows))
            {
                var hit = rows.FirstOrDefault(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }

            // 2) per-code cached resolution
            var cached = cache.GetSingle<Airport>(KeyFor(code));
            if (cached is not null) return cached;

            // 3) resolve-and-cache on first request. Sync-over-async is accepted
            // here: the resolver interface is synchronous (deep inside the
            // flight-builder maths) and minimal APIs run without a sync context.
            var fetched = store
                .QueryAsync<Airport>(DfCollections.Catalogue.Airports, new Dictionary<string, object?> { ["Code"] = code })
                .GetAwaiter().GetResult()
                .FirstOrDefault();

            if (fetched is not null)
                cache.SetSingle(KeyFor(code), fetched);

            return fetched;
        }
    }
}
