using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class LayoutAggregateTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Layout_with_compartments_seats_and_types_round_trips()
    {
        var layouts = new Layouts(fx.Store);
        var id = Guid.NewGuid();
        var economyType = Guid.NewGuid();

        var layout = new Layout
        {
            Id = id,
            CompanyId = DocumentForgeFixture.NewCompany(),
            Name = "A320 single-class",
            Type = "Narrowbody",
            Status = "Active",
            Created = DateTime.UtcNow,
            SeatTypes = new() { new SeatType { Id = economyType, Name = "Economy", Status = "Active" } },
            Compartments = new()
            {
                new LayoutCompartment
                {
                    Id = Guid.NewGuid(), Name = "Economy", Code = "Y", StartRow = 10, EndRow = 30,
                    Columns = "ABCDEF", Status = "Active", DefaultSeatTypeId = economyType,
                    Seats = new()
                    {
                        new Seat { Id = Guid.NewGuid(), RowNumber = 14, Column = "A", Status = "Available", SeatTypeId = economyType },
                        new Seat { Id = Guid.NewGuid(), RowNumber = 14, Column = "B", Status = "Available", SeatTypeId = economyType }
                    }
                }
            }
        };

        await layouts.SaveAsync(layout);
        var got = await layouts.GetByIdAsync(id);

        Assert.NotNull(got);
        Assert.Single(got!.Compartments!);
        Assert.Equal(2, got.Compartments![0].Seats!.Count);
        Assert.Single(got.SeatTypes!);
        Assert.Equal(economyType, got.Compartments![0].Seats![0].SeatTypeId);

        await layouts.DeleteAsync(id, Guid.Empty);
        Assert.Null(await layouts.GetByIdAsync(id));
    }
}
