using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;

namespace AeroBus.Core.Services.Catalogue
{
    public sealed class MarketZoneService(IMarketZones repo, IAirports airports, ICountries countries)
    {
        private readonly IMarketZones _repo = repo;
        private readonly IAirports _airports = airports;
        private readonly ICountries _countries = countries;

        public Task<MarketZone?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            _repo.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<MarketZone>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            _repo.GetByCompanyAsync(companyId, ct);

        public Task<IReadOnlyList<MarketZone>> SearchAsync(Guid? companyId, string? status, string? search, int pageNumber, int pageSize, CancellationToken ct = default) =>
            _repo.SearchAsync(companyId, status, search, pageNumber, pageSize, ct);

        public Task<MarketZone?> SaveAsync(MarketZone model, CancellationToken ct = default) =>
            _repo.SaveAsync(model, ct);

        public Task<bool> DeleteAsync(Guid id, Guid concurrencyId, CancellationToken ct = default) =>
            _repo.DeleteAsync(id, concurrencyId, ct);

        /// <summary>
        /// Compile the zone's embedded selectors into the materialised airport list:
        /// resolve each include rule to airports (Airport→itself, Country→its airports,
        /// Region→its airports, Continent→airports of its countries), union them, then
        /// remove anything matched by an exclude rule. Persists IncludedAirports (CSV of
        /// IATA codes) + IncludedAirportCount.
        /// </summary>
        public async Task<MarketZone?> BuildAsync(Guid id, CancellationToken ct = default)
        {
            var zone = await _repo.GetByIdAsync(id, ct);
            if (zone is null) return null;

            var selectors = zone.Selectors ?? new List<MarketZoneSelector>();
            if (selectors.Count == 0)
                return await _repo.SaveAsync(zone with { IncludedAirports = "", IncludedAirportCount = 0 }, ct);

            var all = await _airports.GetByCompanyAsync(zone.CompanyId ?? Guid.Empty, ct);
            var byId = new Dictionary<Guid, Airport>();
            foreach (var a in all) byId[a.Id] = a;

            async Task<IEnumerable<Guid>> Resolve(MarketZoneSelector s)
            {
                var loc = s.LocationId ?? Guid.Empty;
                switch ((s.LocationType ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "airport": return new[] { loc };
                    case "country": return all.Where(a => a.CountryId == loc).Select(a => a.Id);
                    case "region": return all.Where(a => a.RegionId == loc).Select(a => a.Id);
                    case "continent":
                        var cc = await _countries.GetByContinentAsync(loc, ct);
                        var ids = new HashSet<Guid>(cc.Select(c => c.Id));
                        return all.Where(a => a.CountryId.HasValue && ids.Contains(a.CountryId.Value)).Select(a => a.Id);
                    default: return Array.Empty<Guid>();
                }
            }

            var set = new HashSet<Guid>();
            foreach (var s in selectors.Where(x => x.Included != false)) set.UnionWith(await Resolve(s));
            foreach (var s in selectors.Where(x => x.Included == false)) set.ExceptWith(await Resolve(s));

            var codes = set
                .Select(gid => byId.TryGetValue(gid, out var a) ? a.Code : null)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim().ToUpperInvariant())
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            return await _repo.SaveAsync(
                zone with { IncludedAirports = string.Join(",", codes), IncludedAirportCount = codes.Count }, ct);
        }
    }
}
