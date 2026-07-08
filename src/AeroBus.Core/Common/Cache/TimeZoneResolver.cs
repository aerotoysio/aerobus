namespace AeroBus.Core.Common.Cache
{
    public interface ITimeZoneResolver
    {
        TimeZoneInfo? Resolve(string stationCode);
    }

    /// <summary>
    /// Station → TimeZoneInfo via the (lazy) airport resolver. Returns null when
    /// the station is unknown or has no TimeZoneId, so callers fall back to UTC.
    /// </summary>
    public sealed class CachedTimeZoneResolver(IAirportResolver airports) : ITimeZoneResolver
    {
        public TimeZoneInfo? Resolve(string stationCode)
        {
            var airport = airports.Get(stationCode);
            if (airport == null || string.IsNullOrWhiteSpace(airport.TimeZoneId)) return null;
            return TimeZoneInfo.FindSystemTimeZoneById(airport.TimeZoneId);
        }
    }
}
