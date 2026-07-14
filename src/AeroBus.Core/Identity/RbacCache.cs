namespace AeroBus.Core.Identity
{
    /// <summary>
    /// Version stamp for the custom-RBAC cache. Per-request claim expansion
    /// caches computed permissions under a key that includes the version;
    /// bumping it on any role/assignment change invalidates every cached entry
    /// at once. In-process only — with multiple aerobus instances, other nodes
    /// converge when their entries expire (60s TTL).
    /// </summary>
    public sealed class RbacCacheVersion
    {
        private long _version;
        public long Current => Interlocked.Read(ref _version);
        public void Bump() => Interlocked.Increment(ref _version);
    }
}
