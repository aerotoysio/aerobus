using System.Text.Json.Nodes;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Repositories.Catalogue;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class ProductAggregateTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Product_with_embedded_metadata_round_trips_as_one_document()
    {
        var products = new Products(fx.Store);
        var company = DocumentForgeFixture.NewCompany();
        var id = Guid.NewGuid();

        var product = new Product
        {
            Id = id,
            CompanyId = company,
            Category = "Ancillary",
            ProductType = "Bag",
            Code = "BAG20",
            Name = "20kg Checked Bag",
            Description = "Prepaid checked baggage",
            Data = JsonNode.Parse("""{"weightKg":20}""")!,
            CostAmount = 15.00m,
            CostCurrency = "AUD",
            Tags = "baggage",
            Status = "Active",
            Created = DateTime.UtcNow,
            Metadata = new()
            {
                new ProductMetadata { Id = Guid.NewGuid(), DataName = "Max Weight", DataKey = "weightKg", DataType = "number", Required = 1, Status = "Active" },
                new ProductMetadata { Id = Guid.NewGuid(), DataName = "Cabin Allowed", DataKey = "cabin", DataType = "bool", Required = 0, Status = "Active" }
            }
        };

        await products.SaveAsync(product);

        var got = await products.GetByIdAsync(id);
        Assert.NotNull(got);
        Assert.Equal("BAG20", got!.Code);
        Assert.NotNull(got.Metadata);
        Assert.Equal(2, got.Metadata!.Count);
        Assert.Contains(got.Metadata, m => m.DataKey == "weightKg" && m.Required == 1);

        // filtered, company-scoped list
        var listed = await products.ListByCompanyAsync(company, "Ancillary", null, "Active", null, 1, 50);
        Assert.Contains(listed, p => p.Id == id);

        // update: drop one embedded metadata entry, confirm it persists as one doc
        await products.SaveAsync(got with { Metadata = new() { got.Metadata![0] } });
        Assert.Single((await products.GetByIdAsync(id))!.Metadata!);

        await products.DeleteAsync(id, Guid.Empty);
        Assert.Null(await products.GetByIdAsync(id));
    }
}
