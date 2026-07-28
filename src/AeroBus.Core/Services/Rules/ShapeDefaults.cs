namespace AeroBus.Core.Services.Rules
{
    /// <summary>
    /// The three default input shapes of docs/rule-based-retailing.md, seeded
    /// into the flat <c>shapes</c> collection the first time shapes are listed.
    /// Airlines edit them (and add their own) in AeroStudio settings — these are
    /// starting points, not fixed contracts.
    /// </summary>
    public static class ShapeDefaults
    {
        private const string CustomerAndSearchFields = """
            { "path": "mode", "type": "string", "label": "Request mode", "values": ["included", "optional"], "description": "included = freebies/allowance only; optional = priced extras" },
            { "path": "searchContext.channel", "type": "string", "label": "Sales channel", "values": ["web", "mobile", "agent", "api"] },
            { "path": "searchContext.currency", "type": "string", "label": "Currency" },
            { "path": "searchContext.origin", "type": "string", "label": "Search origin" },
            { "path": "searchContext.destination", "type": "string", "label": "Search destination" },
            { "path": "customers.type", "type": "string", "label": "Passenger type", "values": ["ADT", "CHD", "INF"] },
            { "path": "customers.age", "type": "number", "label": "Passenger age" },
            { "path": "customers.loyaltyTier", "type": "string", "label": "Loyalty tier" },
            { "path": "customers.corporateCode", "type": "string", "label": "Corporate code" },
            { "path": "flightSolution.origin", "type": "string", "label": "Solution origin" },
            { "path": "flightSolution.destination", "type": "string", "label": "Solution destination" },
            { "path": "flightSolution.stops", "type": "number", "label": "Stops" },
            { "path": "flightSolution.tripDurationMinutes", "type": "number", "label": "Trip duration (min)" },
            { "path": "flightSolution.maxStopoverMinutes", "type": "number", "label": "Longest stopover (min)" },
            { "path": "flightSolution.legs.marketingCarrier", "type": "string", "label": "Marketing carrier" },
            { "path": "flightSolution.legs.equipment", "type": "string", "label": "Equipment type" },
            { "path": "flightSolution.legs.cabins.cabin", "type": "string", "label": "Cabin", "values": ["Y", "J", "F"] },
            { "path": "flightSolution.legs.cabins.available", "type": "number", "label": "Seats available" }
            """;

        private const string SampleShoppingCore = """
            "searchContext": { "channel": "web", "currency": "AED", "origin": "DXB", "destination": "LHR" },
            "customers": [
              { "id": "22222222-2222-2222-2222-222222222222", "type": "ADT", "age": 34, "loyaltyTier": "GOLD", "corporateCode": null }
            ],
            "flightSolution": {
              "origin": "DXB", "destination": "LHR",
              "tripDurationMinutes": 420, "maxStopoverMinutes": 0, "stops": 0,
              "legs": [
                {
                  "flightNumber": "VF001", "from": "DXB", "to": "LHR",
                  "departureLocal": "2026-08-01T08:00", "arrivalLocal": "2026-08-01T12:00",
                  "marketingCarrier": "VF", "equipment": "B788",
                  "cabins": [ { "cabin": "Y", "available": 42 } ]
                }
              ]
            }
            """;

        public static string ShoppingJson => $$"""
            {
              "id": "shopping",
              "name": "Shopping Engine",
              "description": "Shape 1 — customers plus a physical flight solution; what shopping rules (RTF eligibility, pricing) receive.",
              "fields": [ {{CustomerAndSearchFields}} ],
              "sample": { "mode": "included", {{SampleShoppingCore}} }
            }
            """;

        public static string RtfBenefitsJson => $$"""
            {
              "id": "rtf-benefits",
              "name": "Right to Fly Benefits",
              "description": "Shape 2 — the shopping shape plus the selected Right to Fly; mode 'included' asks for the freebies/allowance only.",
              "fields": [ {{CustomerAndSearchFields}},
                { "path": "rightToFly.code", "type": "string", "label": "Right to Fly code" }
              ],
              "sample": { "mode": "included", "rightToFly": { "code": "SAVER", "name": "Saver" }, {{SampleShoppingCore}} }
            }
            """;

        public static string ALaCarteJson => $$"""
            {
              "id": "a-la-carte",
              "name": "A La Carte",
              "description": "Shape 3 — same as Benefits with mode 'optional' (and an optional maxSpend): the priced extras.",
              "fields": [ {{CustomerAndSearchFields}},
                { "path": "rightToFly.code", "type": "string", "label": "Right to Fly code" },
                { "path": "maxSpend", "type": "number", "label": "Max spend" }
              ],
              "sample": { "mode": "optional", "rightToFly": { "code": "SAVER", "name": "Saver" }, {{SampleShoppingCore}} }
            }
            """;

        public static IReadOnlyList<string> All => [ShoppingJson, RtfBenefitsJson, ALaCarteJson];
    }
}
