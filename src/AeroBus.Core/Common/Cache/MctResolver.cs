using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Common.Cache
{
    public interface IMctResolver
    {
        (TimeSpan Min, TimeSpan Max)? Resolve(string airportCode, string connType = "DD", string? fromTerminal = null, string? toTerminal = null, string? carrier = null, Guid? companyId = null);
    }

    public sealed class CachedMctResolver(IHotCache cache, IDocumentStore store) : IMctResolver
    {
        public (TimeSpan Min, TimeSpan Max)? Resolve(string airportCode, string connType = "DD",
            string? fromTerminal = null, string? toTerminal = null, string? carrier = null, Guid? companyId = null)
        {
            // Lazy load: if no connection-rule snapshot exists yet, pull the whole
            // (small) collection once and cache it. An empty collection still
            // creates the bucket, so we don't re-query on every miss. A null MCT
            // lets callers fall back to their defaults.
            if (!cache.TryGet<ConnectionRule>(CacheKeys.Connection, out var rules))
            {
                var loaded = store
                    .QueryAsync<ConnectionRule>("connectionrules")
                    .GetAwaiter().GetResult();
                cache.Set(CacheKeys.Connection, loaded);
                rules = loaded;
            }
            if (rules.Count == 0)
                return null;

            // helper
            bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

            // A rule with no/Default/ANY ConnType applies to any connection type; a specific ConnType must match.
            bool ConnTypeApplies(string? ruleType) =>
                string.IsNullOrWhiteSpace(ruleType) || Eq(ruleType, "Default") || Eq(ruleType, "ANY") || Eq(ruleType, connType);

            IEnumerable<ConnectionRule> cand = rules.Where(r =>
                Eq(r.AirportCode, airportCode) &&
                ConnTypeApplies(r.ConnType) &&
                Eq(r.Status, "Active"));

            // Company-specific first, then global
            var ordered = cand
                .OrderByDescending(r => r.CompanyId.HasValue && companyId.HasValue && r.CompanyId == companyId)   // company match
                .ThenByDescending(r => !r.CompanyId.HasValue && !companyId.HasValue) // both null (global)
                .ThenByDescending(r => Eq(r.ConnType, connType)) // exact ConnType beats a Default/wildcard rule
                .ThenByDescending(r => r.FromTerminal != null && r.ToTerminal != null) // both terminals
                .ThenByDescending(r => r.Carrier != null && carrier != null && Eq(r.Carrier, carrier)) // carrier match
                .ThenByDescending(r => r.FromTerminal != null || r.ToTerminal != null) // any terminal given
                .ThenByDescending(r => r.Carrier != null) // any carrier specified
                .ThenByDescending(r => r.Alliance != null); // alliance as lowest signal

            var rule = ordered.FirstOrDefault(r =>
                (r.FromTerminal == null || Eq(r.FromTerminal, fromTerminal)) &&
                (r.ToTerminal == null || Eq(r.ToTerminal, toTerminal)) &&
                (r.Carrier == null || Eq(r.Carrier, carrier)) &&
                (!r.CompanyId.HasValue || companyId.HasValue && r.CompanyId == companyId));

            if (rule is null) return null;
            return (TimeSpan.FromMinutes(rule.MinMinutes), TimeSpan.FromMinutes(rule.MaxMinutes));
        }
    }
}
