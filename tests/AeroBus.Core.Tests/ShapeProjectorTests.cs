using System.Text.Json;
using AeroBus.Core.Services.Rules;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// The projection layer: different caller shapes produce the SAME canonical
/// policy contract, so one policy serves them all (docs/rule-based-retailing.md).
/// </summary>
public class ShapeProjectorTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly JsonElement Canonical = Parse(ShapeDefaults.PolicyInputJson);

    [Fact]
    public void Array_shape_projects_identically_including_all_customers()
    {
        var shape = Parse(ShapeDefaults.RtfBenefitsJson); // paths match canonical → implicit mapping
        var request = Parse("""
            {
              "mode": "included",
              "rightToFly": { "code": "SAVER" },
              "searchContext": { "channel": "web", "currency": "USD", "origin": "DXB", "destination": "LHR" },
              "customers": [
                { "id": "p1", "type": "ADT", "age": 40, "loyaltyTier": "G" },
                { "id": "p2", "type": "ADT", "age": 33, "loyaltyTier": "B" }
              ],
              "flightSolution": { "origin": "DXB", "destination": "LHR", "stops": 0,
                "legs": [ { "equipment": "B788", "cabins": [ { "cabin": "Y", "available": 12 } ] } ] }
            }
            """);

        var result = ShapeProjector.Project(request, shape, Canonical);

        Assert.Equal("included", result.GetProperty("mode").GetString());
        Assert.Equal("SAVER", result.GetProperty("rightToFly").GetProperty("code").GetString());
        var customers = result.GetProperty("customers");
        Assert.Equal(2, customers.GetArrayLength());
        Assert.Equal("G", customers[0].GetProperty("loyaltyTier").GetString());
        Assert.Equal("B", customers[1].GetProperty("loyaltyTier").GetString());
        Assert.Equal("DXB", result.GetProperty("flightSolution").GetProperty("origin").GetString());
        // paxIds derived from customers[].id
        Assert.Equal(2, result.GetProperty("paxIds").GetArrayLength());
    }

    [Fact]
    public void Singleton_shape_lifts_into_a_one_element_customers_array()
    {
        // A caller whose request carries ONE customer object under different
        // paths — mapped to the canonical contract via explicit concepts.
        var shape = Parse("""
            {
              "id": "single-customer",
              "name": "Single customer caller",
              "fields": [
                { "path": "mode", "type": "string" },
                { "path": "customer.tier", "type": "string", "concept": "customers.loyaltyTier" },
                { "path": "customer.paxType", "type": "string", "concept": "customers.type" },
                { "path": "trip.from", "type": "string", "concept": "flightSolution.origin" }
              ]
            }
            """);
        var request = Parse("""
            {
              "mode": "included",
              "customer": { "tier": "G", "paxType": "ADT" },
              "trip": { "from": "DXB" }
            }
            """);

        var result = ShapeProjector.Project(request, shape, Canonical);

        var customers = result.GetProperty("customers");
        Assert.Equal(1, customers.GetArrayLength());
        Assert.Equal("G", customers[0].GetProperty("loyaltyTier").GetString());
        Assert.Equal("ADT", customers[0].GetProperty("type").GetString());
        Assert.Equal("DXB", result.GetProperty("flightSolution").GetProperty("origin").GetString());
        Assert.Equal("included", result.GetProperty("mode").GetString());
    }

    [Fact]
    public void Unmapped_and_unknown_fields_are_skipped_not_fatal()
    {
        var shape = Parse("""
            {
              "id": "sparse",
              "fields": [
                { "path": "mode", "type": "string" },
                { "path": "somewhere.else", "type": "string", "concept": "not.a.canonical.path" },
                { "path": "missing.in.request", "type": "string", "concept": "customers.loyaltyTier" }
              ]
            }
            """);
        var request = Parse("""{ "mode": "optional" }""");

        var result = ShapeProjector.Project(request, shape, Canonical);

        Assert.Equal("optional", result.GetProperty("mode").GetString());
        Assert.False(result.TryGetProperty("customers", out _));
        Assert.False(result.TryGetProperty("not", out _));
    }
}
