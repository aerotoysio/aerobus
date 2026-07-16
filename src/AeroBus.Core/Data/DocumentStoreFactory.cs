using Microsoft.Extensions.Options;

namespace AeroBus.Core.Data
{
    /// <summary>
    /// Builds a document client/store bound to an <b>explicit</b> database, bypassing
    /// per-request tenant routing. Used by provisioning to seed a brand-new org
    /// database (the org isn't in the registry yet and the onboarding request is
    /// anonymous, so the auto-routed client can't reach it).
    /// </summary>
    public interface IDocumentStoreFactory
    {
        IDocumentForgeClient ClientForDatabase(string database);
        IDocumentStore ForDatabase(string database);
    }

    public sealed class DocumentStoreFactory(IHttpClientFactory httpClientFactory, IOptions<DocumentForgeOptions> options)
        : IDocumentStoreFactory
    {
        public IDocumentForgeClient ClientForDatabase(string database) =>
            new DocumentForgeClient(
                httpClientFactory.CreateClient(ServiceCollectionExtensions.MainClientName),
                options.Value,
                database);

        public IDocumentStore ForDatabase(string database) => new DocumentStore(ClientForDatabase(database));
    }
}
