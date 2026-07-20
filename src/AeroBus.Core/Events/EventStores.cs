using AeroBus.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Events
{
    /// <summary>
    /// Picks where this request's event data lives. Events are airline-specific:
    /// an authenticated org call reads and writes the outbox / cursors / webhook
    /// subscriptions in the org's OWN database (via the tenant-routed client), so
    /// one airline's event stream never mixes with another's. Anything without a
    /// resolved tenant — provisioning, platform-admin calls, background paths —
    /// uses the control database, which holds only platform-level events
    /// (org.created and friends).
    /// </summary>
    public interface IEventStores
    {
        IDocumentStore Store { get; }
        IDocumentForgeClient Client { get; }

        /// <summary>
        /// Partition key for the publisher's process-wide cursor-id cache: the same
        /// company allocates sequences in different databases depending on context
        /// (its own db for domain events, control for platform events), so cached
        /// DocumentForge _ids must never leak across databases.
        /// </summary>
        string DatabaseKey { get; }
    }

    public sealed class EventStores : IEventStores
    {
        private const string ControlKey = Data.ServiceCollectionExtensions.ControlClientKey;

        private readonly ITenantDatabase _tenant;
        private readonly IDocumentStore _main;
        private readonly IDocumentForgeClient _mainClient;
        private readonly IDocumentStore _control;
        private readonly IDocumentForgeClient _controlClient;

        public EventStores(
            ITenantDatabase tenant,
            IDocumentStore main,
            IDocumentForgeClient mainClient,
            [FromKeyedServices(ControlKey)] IDocumentStore control,
            [FromKeyedServices(ControlKey)] IDocumentForgeClient controlClient)
        {
            _tenant = tenant;
            _main = main;
            _mainClient = mainClient;
            _control = control;
            _controlClient = controlClient;
        }

        public IDocumentStore Store => _tenant.IsTenantResolved ? _main : _control;
        public IDocumentForgeClient Client => _tenant.IsTenantResolved ? _mainClient : _controlClient;
        public string DatabaseKey => _tenant.IsTenantResolved ? _tenant.CurrentDatabase! : "control";
    }
}
