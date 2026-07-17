using AeroBus.Core.Data;
using AeroBus.Core.Model.Catalogue;

namespace AeroBus.Core.Services.Admin
{
    /// <summary>
    /// The starter reference pack seeded into a newly-provisioned org database so the
    /// airline can immediately create a schedule and build a flight. Deliberately
    /// minimal (a few hub airports + one aircraft type) — a full reference-data
    /// import is a separate concern. Everything is stamped with the org's CompanyId
    /// (data still carries the organisation even though the database is per-tenant).
    /// </summary>
    public static class ReferenceSeed
    {
        private static readonly (string Code, string Name, string City, string Tz)[] Airports =
        [
            ("DXB", "Dubai International", "Dubai", "Asia/Dubai"),
            ("LHR", "London Heathrow", "London", "Europe/London"),
            ("JFK", "John F. Kennedy International", "New York", "America/New_York"),
            ("SIN", "Singapore Changi", "Singapore", "Asia/Singapore"),
            ("SYD", "Sydney Kingsford Smith", "Sydney", "Australia/Sydney"),
        ];

        public static async Task SeedAsync(IDocumentStore store, Guid companyId, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            foreach (var (code, name, city, tz) in Airports)
            {
                var airport = new Airport
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Name = name,
                    City = city,
                    TimeZoneId = tz,
                    Status = "Active",
                    Created = now,
                    CompanyId = companyId,
                };
                await store.UpsertAsync(DfCollections.Catalogue.Airports, airport, airport.Id, ct);
            }

            var equipment = new Equipment
            {
                Id = Guid.NewGuid(),
                EquipmentCode = "320",
                Name = "Airbus A320",
                RangeNm = 3300,
                Status = "Active",
                Created = now,
                CompanyId = companyId,
            };
            await store.UpsertAsync(DfCollections.Catalogue.Equipment, equipment, equipment.Id, ct);
        }
    }
}
