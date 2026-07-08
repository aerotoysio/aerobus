using System.Collections.Concurrent;

namespace AeroBus.Core.Common.Cache
{
    /// <summary>
    /// Well-known hot-cache bucket keys. (Pricing/offer keys from ooms are gone
    /// with those modules; the catalogue/shopping keys remain.)
    /// </summary>
    public static class CacheKeys
    {
        public const string Company = "company";
        public const string Flights = "flights";
        public const string Connection = "connection";
        public const string Continents = "continents";
        public const string Countries = "countries";
        public const string Regions = "regions";
        public const string Airports = "airports";
        public const string Equipment = "equipment";
        public const string TimeZones = "timezones";

        public const string Attributes = "catalogue.attributes";
        public const string Layouts = "catalogue.layouts";
        public const string Media = "catalogue.media";
        public const string Products = "catalogue.products";
        public const string Bundles = "catalogue.bundles";
        public const string ProductMetadata = "catalogue.productMetadata";
        public const string Schedules = "catalogue.schedules";
        public const string MarketZones = "catalogue.marketZones";
    }

    /// <summary>
    /// Process-local snapshot cache: whole-list buckets swapped atomically.
    /// In-memory only — the ooms Redis/StackExchange path is deliberately not
    /// ported. There is no boot-time preloader either: consumers (resolvers)
    /// populate buckets lazily on first request.
    /// </summary>
    public interface IHotCache
    {
        // Set/replace with already-fetched items
        void Set<T>(string key, IReadOnlyList<T> items);

        // Set/replace a single-item bucket
        void SetSingle<T>(string key, T item);

        // Load via async loader and swap in one shot
        Task LoadAsync<T>(string key, Func<CancellationToken, Task<IReadOnlyList<T>>> loader,
                          CancellationToken ct = default);

        // Get a snapshot (throws if type/key mismatch)
        IReadOnlyList<T> Get<T>(string key);

        // Get a single-item snapshot (default when missing)
        T? GetSingle<T>(string key);

        // Try variant
        bool TryGet<T>(string key, out IReadOnlyList<T> items);

        // Clear one key or all
        void Clear(string? key = null);

        // Optional: metadata
        (DateTimeOffset? LoadedAtUtc, int Count) Info(string key);
    }

    internal interface ICacheBucket
    {
        DateTimeOffset? LoadedAtUtc { get; }
        int Count { get; }
        object RawSnapshot { get; }
    }

    internal sealed class CacheBucket<T> : ICacheBucket
    {
        private T[] _items = Array.Empty<T>(); // volatile by reference semantics
        private DateTimeOffset? _loadedAtUtc;

        public void Swap(IReadOnlyList<T> items)
        {
            var arr = items is T[] a ? a : items.ToArray();
            Interlocked.Exchange(ref _items, arr);
            _loadedAtUtc = DateTimeOffset.UtcNow;
        }

        public IReadOnlyList<T> Snapshot() => _items;
        public DateTimeOffset? LoadedAtUtc => _loadedAtUtc;
        public int Count => _items.Length;
        object ICacheBucket.RawSnapshot => _items;
    }

    public sealed class HotCache : IHotCache
    {
        private readonly ConcurrentDictionary<string, ICacheBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

        public void Set<T>(string key, IReadOnlyList<T> items)
        {
            var bucket = (CacheBucket<T>)_buckets.GetOrAdd(key, _ => new CacheBucket<T>());
            bucket.Swap(items);
        }

        public async Task LoadAsync<T>(string key, Func<CancellationToken, Task<IReadOnlyList<T>>> loader,
                                       CancellationToken ct = default)
        {
            var data = await loader(ct).ConfigureAwait(false);
            Set(key, data);
        }

        public IReadOnlyList<T> Get<T>(string key)
        {
            if (!_buckets.TryGetValue(key, out var b))
                throw new KeyNotFoundException($"HotCache key '{key}' not found.");

            if (b is CacheBucket<T> typed)
                return typed.Snapshot();

            throw new InvalidOperationException($"HotCache key '{key}' is not of type {typeof(T).Name}.");
        }

        public bool TryGet<T>(string key, out IReadOnlyList<T> items)
        {
            items = Array.Empty<T>();
            if (!_buckets.TryGetValue(key, out var b)) return false;
            if (b is CacheBucket<T> typed)
            {
                items = typed.Snapshot();
                return true;
            }
            return false;
        }

        public void Clear(string? key = null)
        {
            if (key is null) { _buckets.Clear(); return; }
            _buckets.TryRemove(key, out _);
        }

        public (DateTimeOffset? LoadedAtUtc, int Count) Info(string key)
        {
            if (_buckets.TryGetValue(key, out var b))
                return (b.LoadedAtUtc, b.Count);
            return (null, 0);
        }

        public IEnumerable<(string Key, Type ElementType, int Count, DateTimeOffset? LoadedAtUtc)> Inspect()
        {
            foreach (var (key, bucket) in _buckets)
            {
                var raw = bucket.RawSnapshot;                 // this is T[] boxed
                var elemType = raw?.GetType().GetElementType() ?? typeof(object);
                yield return (key, elemType, bucket.Count, bucket.LoadedAtUtc);
            }
        }

        public T? GetSingle<T>(string key)
        {
            if (_buckets.TryGetValue(key, out var bucket) && bucket is CacheBucket<T> typed)
                return typed.Snapshot().FirstOrDefault();
            return default;
        }

        public void SetSingle<T>(string key, T item)
        {
            var bucket = (CacheBucket<T>)_buckets.GetOrAdd(key, _ => new CacheBucket<T>());
            if (item is null)
            {
                // store empty
                bucket.Swap(Array.Empty<T>());
            }
            else
            {
                // store a 1-element snapshot
                bucket.Swap(new[] { item });
            }
        }
    }
}
