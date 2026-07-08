using AeroBus.Core.Common.Cache;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using AeroBus.Core.Repositories.Stock;
using Xunit;

namespace AeroBus.Core.Tests;

file sealed class UtcTz : ITimeZoneResolver { public TimeZoneInfo? Resolve(string stationCode) => TimeZoneInfo.Utc; }

[Collection("documentforge")]
public class FlightExtractionTests(DocumentForgeFixture fx)
{
    private (ISchedules schedules, IFlights flights, IFlightInventories inventories, IFlightBuilder builder) Build()
    {
        var schedules = new Schedules(fx.Store);
        var flights = new Flights(fx.Store);
        var layouts = new Layouts(fx.Store);
        var inventories = new FlightInventories(fx.Store);
        var builder = new FlightBuilder(schedules, flights, layouts, inventories, new UtcTz());
        return (schedules, flights, inventories, builder);
    }

    [Fact]
    public async Task Schedule_extracts_into_one_flight_per_operating_day_with_counters()
    {
        var (schedules, flights, inventories, builder) = Build();

        var companyId = DocumentForgeFixture.NewCompany();
        var start = new DateTime(2026, 6, 1);
        var sched = new Schedule
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            LayoutId = Guid.NewGuid(),                 // no such layout → single "ALL" bucket
            CarrierCode = "AT",
            FlightNumber = "100",
            DepartureStation = "BNE",
            ArrivalStation = "SYD",
            DepartureTimeLocal = new TimeSpan(8, 0, 0),
            ArrivalTimeLocal = new TimeSpan(10, 0, 0),
            ArrivalOffsetDays = 0,
            StartDateLocal = start,
            EndDateLocal = start.AddDays(6),       // 7-day window
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true,
            Status = "Approved",
            Created = DateTime.UtcNow,
            Tags = "",
            Capacity = 180
        };
        await schedules.SaveAsync(sched);

        var built = await builder.BuildAsync(sched.Id);

        Assert.Equal(7, built.Count);                                  // one flight per operating day
        Assert.All(built, f => Assert.Equal(sched.Id, f.ScheduleId));  // linked to schedule
        Assert.All(built, f => Assert.NotNull(f.Counters));            // counters embedded
        Assert.Equal(180, built[0].Counters!.Capacity);
        Assert.Equal(180, built[0].Counters!.Available);

        var after = await schedules.GetByIdAsync(sched.Id);
        Assert.Equal("Built", after!.Status);                          // schedule marked Built

        // also reachable via the by-schedule query
        var bySchedule = await flights.GetByScheduleIdAsync(sched.Id);
        Assert.Equal(7, bySchedule.Count);

        // NEW behaviour: each flight got its inventory document(s). No layout on
        // this schedule → a single "ALL" bucket sized from Schedule.Capacity.
        foreach (var f in built)
        {
            var inv = await inventories.GetByFlightAsync(f.Id);
            var bucket = Assert.Single(inv);
            Assert.Equal("ALL", bucket.Bucket);
            Assert.Equal(180, bucket.Capacity);
            Assert.Equal(0, bucket.Sold);
            Assert.Equal(180, bucket.Available);
            Assert.Equal(companyId, bucket.CompanyId);
        }

        foreach (var f in built)
        {
            foreach (var inv in await inventories.GetByFlightAsync(f.Id))
                await inventories.DeleteAsync(inv.Id, Guid.Empty);
            await flights.DeleteAsync(f.Id, Guid.Empty);
        }
        await schedules.DeleteAsync(sched.Id, Guid.Empty);
    }
}
