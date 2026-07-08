using AeroBus.Core.Events;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// The headline Phase-6 correctness test (mirrors the Phase-5 oversell test): under
/// M concurrent PublishAsync calls for the SAME company, every allocated
/// <see cref="OutboxEvent.Seq"/> is distinct AND contiguous — no gaps, no dupes.
/// This is what makes a Seq-cursor stream (<c>?from={seq}</c>) resumable under
/// concurrency, and it rests entirely on the atomic conditional-inc against the
/// per-company eventcursors document.
/// </summary>
[Collection("documentforge")]
public class EventSequenceTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Concurrent_publishes_allocate_distinct_contiguous_seqs()
    {
        var company = DocumentForgeFixture.NewCompany();
        var publisher = EventsTestHelpers.Publisher(fx);
        const int contenders = 40;

        // Fire M publishes at once for the same company.
        var tasks = Enumerable.Range(0, contenders)
            .Select(i => publisher.PublishAsync(
                "test.event",
                new EventSubject("tests", Guid.NewGuid().ToString()),
                new { i },
                company))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var seqs = results.Select(r => r!.Seq).OrderBy(s => s).ToList();

        // Every publish succeeded and carries a Seq.
        Assert.Equal(contenders, results.Count(r => r is not null));

        // Distinct: no two publishes got the same Seq.
        Assert.Equal(contenders, seqs.Distinct().Count());

        // Contiguous 1..N: first is 1 (fresh company cursor), no gaps.
        Assert.Equal(1, seqs.First());
        Assert.Equal(contenders, seqs.Last());
        for (var i = 0; i < seqs.Count; i++)
            Assert.Equal(i + 1, seqs[i]);

        // And they are genuinely persisted in the outbox at those seqs.
        var outbox = EventsTestHelpers.Outbox(fx);
        var persisted = await outbox.ListForCompanyAsync(company, type: null, status: null, fromSeq: 0, limit: 500);
        Assert.Equal(contenders, persisted.Count);

        // Cleanup.
        foreach (var e in persisted) await outbox.DeleteAsync(e.Id);
    }

    [Fact]
    public async Task Seqs_are_isolated_per_company()
    {
        var companyA = DocumentForgeFixture.NewCompany();
        var companyB = DocumentForgeFixture.NewCompany();
        var publisher = EventsTestHelpers.Publisher(fx);

        var a1 = await publisher.PublishAsync("t", new EventSubject("t", "1"), new { }, companyA);
        var b1 = await publisher.PublishAsync("t", new EventSubject("t", "1"), new { }, companyB);
        var a2 = await publisher.PublishAsync("t", new EventSubject("t", "2"), new { }, companyA);

        // Each company's cursor counts independently from 1.
        Assert.Equal(1, a1!.Seq);
        Assert.Equal(1, b1!.Seq);
        Assert.Equal(2, a2!.Seq);

        var outbox = EventsTestHelpers.Outbox(fx);
        foreach (var c in new[] { companyA, companyB })
            foreach (var e in await outbox.ListForCompanyAsync(c, null, null, 0, 500))
                await outbox.DeleteAsync(e.Id);
    }
}
