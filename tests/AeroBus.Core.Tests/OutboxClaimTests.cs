using AeroBus.Core.Events;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Multi-instance safety for dispatch: two dispatchers racing to claim the SAME
/// Pending outbox row must not both win. The claim is a DocumentForge
/// conditional-update guarded on Status, so exactly one CAS succeeds and the other
/// 409s (and skips). Without this a row could be delivered twice concurrently.
/// </summary>
[Collection("documentforge")]
public class OutboxClaimTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Two_concurrent_claims_on_one_pending_row_exactly_one_wins()
    {
        var company = DocumentForgeFixture.NewCompany();
        var publisher = EventsTestHelpers.Publisher(fx);
        var outbox = EventsTestHelpers.Outbox(fx);

        // Publish one Pending row.
        var evt = await publisher.PublishAsync(
            "claim.test", new EventSubject("tests", Guid.NewGuid().ToString()), new { }, company);
        Assert.NotNull(evt);

        var row = await outbox.GetByIdAsync(evt!.Id);
        Assert.NotNull(row);
        Assert.Equal(OutboxStatus.Pending, row!.Status);

        // Two instances try to claim the same row at the same time.
        var claimA = outbox.TryClaimAsync(row);
        var claimB = outbox.TryClaimAsync(row);
        var results = await Task.WhenAll(claimA, claimB);

        // Exactly one wins the CAS.
        Assert.Equal(1, results.Count(won => won));

        // The row is now Dispatching with Attempts bumped exactly once.
        var after = await outbox.GetByIdAsync(evt.Id);
        Assert.NotNull(after);
        Assert.Equal(OutboxStatus.Dispatching, after!.Status);
        Assert.Equal(1, after.Attempts);

        // A third claim (status no longer Pending) also loses.
        Assert.False(await outbox.TryClaimAsync(row));

        await outbox.DeleteAsync(evt.Id);
    }

    [Fact]
    public async Task Many_concurrent_claims_yield_exactly_one_winner()
    {
        var company = DocumentForgeFixture.NewCompany();
        var publisher = EventsTestHelpers.Publisher(fx);
        var outbox = EventsTestHelpers.Outbox(fx);

        var evt = await publisher.PublishAsync(
            "claim.test", new EventSubject("tests", Guid.NewGuid().ToString()), new { }, company);
        var row = await outbox.GetByIdAsync(evt!.Id);

        var tasks = Enumerable.Range(0, 12).Select(_ => outbox.TryClaimAsync(row!)).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(won => won));

        var after = await outbox.GetByIdAsync(evt.Id);
        Assert.Equal(1, after!.Attempts); // only the single winning CAS bumped Attempts

        await outbox.DeleteAsync(evt.Id);
    }
}
