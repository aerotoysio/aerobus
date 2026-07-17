using AeroBus.Core.Data;
using AeroBus.Core.Model.Admin;

namespace AeroBus.Core.Common.Cache
{
    public interface ICompanyResolver
    {
        // Gets the company (and thereby its embedded configs)
        Company? Resolve(Guid companyId);
    }

    /// <summary>
    /// Lazy company lookup keyed by id: resolve-and-cache on first request.
    /// (The ooms version returned the single boot-preloaded company regardless
    /// of the id argument; the AeroBus version resolves the id it is given.)
    /// </summary>
    public sealed class CachedCompanyResolver(IHotCache cache, IDocumentStore store) : ICompanyResolver
    {
        private static string KeyFor(Guid id) => $"company.{id:N}";

        public Company? Resolve(Guid companyId)
        {
            if (companyId == Guid.Empty) return null;

            var cached = cache.GetSingle<Company>(KeyFor(companyId));
            if (cached is not null) return cached;

            var fetched = store
                .GetByIdAsync<Company>(DfCollections.Admin.Companies, companyId)
                .GetAwaiter().GetResult();

            if (fetched is not null)
                cache.SetSingle(KeyFor(companyId), fetched);

            return fetched;
        }
    }
}
