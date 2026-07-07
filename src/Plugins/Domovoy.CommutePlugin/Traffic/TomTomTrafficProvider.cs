using System.Globalization;
using System.Text.Json;

using Domovoy.CommutePlugin.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.CommutePlugin.Traffic;

/// <summary>
/// Live traffic via the TomTom Routing API (free tier ~2.5k req/day, includes live traffic and
/// <c>arriveAt</c> for the deadline case). Called only when a key is configured; any failure returns null so
/// the planner degrades to its last-known / offline behaviour rather than throwing. Kept deliberately thin —
/// swapping in HERE or a paid Yandex-traffic key is a new class implementing <see cref="ITrafficProvider"/>.
/// </summary>
public sealed class TomTomTrafficProvider : ITrafficProvider
{
    private const string BaseUrl = "https://api.tomtom.com/routing/1/calculateRoute/";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<TomTomTrafficProvider> _logger;

    public TomTomTrafficProvider(HttpClient http, string apiKey, ILogger<TomTomTrafficProvider> logger)
    {
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
    }

    public string Name => "tomtom";

    public async Task<RouteEstimate?> EstimateAsync(
        GeoPoint from, GeoPoint to, DateTimeOffset now, DateTimeOffset? arriveBy, CancellationToken ct)
    {
        try
        {
            var locations = $"{Coord(from.Latitude)},{Coord(from.Longitude)}:{Coord(to.Latitude)},{Coord(to.Longitude)}";
            var url = $"{BaseUrl}{locations}/json?key={Uri.EscapeDataString(_apiKey)}"
                      + "&traffic=true&travelMode=car&computeTravelTimeFor=all&routeType=fastest";
            // arriveAt makes TomTom predict traffic for the target arrival time (deadline mode).
            if (arriveBy is { } deadline)
                url += $"&arriveAt={Uri.EscapeDataString(deadline.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture))}";

            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            var summary = doc.RootElement.GetProperty("routes")[0].GetProperty("summary");

            double travelSeconds = summary.GetProperty("travelTimeInSeconds").GetDouble();
            double lengthMeters = summary.GetProperty("lengthInMeters").GetDouble();
            double delaySeconds = summary.TryGetProperty("trafficDelayInSeconds", out var d) ? d.GetDouble() : 0;

            var travelMinutes = travelSeconds / 60.0;
            var delayMinutes = delaySeconds / 60.0;
            var congestion = travelMinutes > delayMinutes && travelMinutes > 0
                ? travelMinutes / (travelMinutes - delayMinutes)
                : 1.0;

            return new RouteEstimate(
                DistanceKm: Math.Round(lengthMeters / 1000.0, 1),
                TravelMinutes: Math.Round(travelMinutes, 1),
                TrafficDelayMinutes: Math.Round(delayMinutes, 1),
                TrafficLevel: SimulatedTrafficProvider.LevelFor(congestion));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TomTom estimate failed; planner will fall back");
            return null;
        }
    }

    private static string Coord(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
