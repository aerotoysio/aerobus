using AeroBus.Core.Data;
using AeroBus.Core.Repositories.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Events
{
    /// <summary>One database the outbox pump drains: its outbox + the webhook subscriptions that live beside it.</summary>
    public sealed record EventPumpTarget(string Database, IOutbox Outbox, IWebhookSubscriptions Subscriptions);

    /// <summary>
    /// Where the dispatcher finds event databases to pump. Events are
    /// airline-specific (each org's outbox lives in its own database), so the
    /// production source yields control (platform events) plus every Active org
    /// from the registry; tests substitute a fixed single-database source.
    /// </summary>
    public interface IEventPumpTargets
    {
        Task<IReadOnlyList<EventPumpTarget>> GetAsync(CancellationToken ct = default);
    }

    /// <summary>Production source: the control database, then every Active registered org's own database.</summary>
    public sealed class RegistryEventPumpTargets : IEventPumpTargets
    {
        private const string ControlKey = Data.ServiceCollectionExtensions.ControlClientKey;

        private readonly IOrganisations _orgs;
        private readonly IDocumentStoreFactory _factory;
        private readonly IDocumentStore _controlStore;
        private readonly IDocumentForgeClient _controlClient;

        public RegistryEventPumpTargets(
            IOrganisations orgs,
            IDocumentStoreFactory factory,
            [FromKeyedServices(ControlKey)] IDocumentStore controlStore,
            [FromKeyedServices(ControlKey)] IDocumentForgeClient controlClient)
        {
            _orgs = orgs;
            _factory = factory;
            _controlStore = controlStore;
            _controlClient = controlClient;
        }

        public async Task<IReadOnlyList<EventPumpTarget>> GetAsync(CancellationToken ct = default)
        {
            var targets = new List<EventPumpTarget>
            {
                new("control", new Outbox(_controlStore, _controlClient), new WebhookSubscriptions(_controlStore)),
            };

            // A registry read failure propagates to the dispatcher's outer
            // backoff (it means the store itself is unreachable).
            foreach (var org in await _orgs.GetAllAsync(ct))
            {
                if (!string.Equals(org.Status, "Active", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(org.ShortName)) continue;

                var store = _factory.ForDatabase(org.ShortName);
                var client = _factory.ClientForDatabase(org.ShortName);
                targets.Add(new(org.ShortName, new Outbox(store, client), new WebhookSubscriptions(store)));
            }
            return targets;
        }
    }

    /// <summary>Fixed target list — the single-database source tests (and dev harnesses) use.</summary>
    public sealed class StaticEventPumpTargets(params EventPumpTarget[] targets) : IEventPumpTargets
    {
        private readonly IReadOnlyList<EventPumpTarget> _targets = targets;

        public Task<IReadOnlyList<EventPumpTarget>> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(_targets);
    }
}
