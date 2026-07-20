using AeroBus.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace AeroBus.Core.Tests;

/// <summary>
/// Shared construction helpers for the events backbone in tests. A real
/// <see cref="EventPublisher"/> over the fixture's DocumentForge is used
/// everywhere (rather than a no-op) so the existing lifecycle/extraction tests
/// also exercise real Seq allocation + outbox inserts as a side effect.
/// </summary>
internal static class EventsTestHelpers
{
    public static EventPublisher Publisher(DocumentForgeFixture fx) =>
        new(fx.Client, fx.Store, "test", NullLogger<EventPublisher>.Instance);

    public static Outbox Outbox(DocumentForgeFixture fx) =>
        new(fx.Store, fx.Client);
}
