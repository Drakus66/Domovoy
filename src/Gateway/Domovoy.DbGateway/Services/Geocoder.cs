using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domovoy.DbGateway.Services;

/// <summary>One geocoder candidate: a place name resolved to coordinates.</summary>
public sealed record GeocodeResult(string Label, double Latitude, double Longitude);

/// <summary>
/// Turns a place name (or a map click) into coordinates while the user is editing the site location
/// (roadmap Epic 2K). This is the <b>only</b> point in the location feature that touches the network, and
/// it is strictly optional: sunrise/sunset and the timezone are computed locally from coordinates, so an
/// offline install simply enters latitude/longitude by hand and never calls this. Provider-agnostic by
/// design (the default backing is OpenStreetMap Nominatim); a failure degrades to an empty result set
/// rather than an error, so the UI can always fall back to manual entry.
/// </summary>
public interface IGeocoder
{
    /// <summary>Forward-geocode a free-text query → candidate places (empty when offline or nothing matched).</summary>
    Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, CancellationToken ct);

    /// <summary>Reverse-geocode coordinates → a display label, or null when offline / not found.</summary>
    Task<string?> ReverseAsync(double latitude, double longitude, CancellationToken ct);
}

/// <summary>
/// OpenStreetMap Nominatim geocoder. Uses the public endpoint; per Nominatim usage policy it sends an
/// identifying User-Agent and asks for at most a handful of results. All faults are swallowed to an empty
/// result — network outages must not surface as errors in the settings UI (offline-first).
/// </summary>
public sealed class NominatimGeocoder : IGeocoder
{
    private readonly HttpClient _http;
    private readonly ILogger<NominatimGeocoder> _logger;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NominatimGeocoder(HttpClient http, ILogger<NominatimGeocoder> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GeocodeResult>();

        try
        {
            var url = $"search?format=jsonv2&limit=5&q={Uri.EscapeDataString(query.Trim())}";
            var hits = await _http.GetFromJsonAsync<List<NominatimPlace>>(url, Json, ct);
            if (hits is null) return Array.Empty<GeocodeResult>();

            var results = new List<GeocodeResult>(hits.Count);
            foreach (var h in hits)
            {
                if (TryParse(h.Lat, out var lat) && TryParse(h.Lon, out var lon))
                    results.Add(new GeocodeResult(h.DisplayName ?? query.Trim(), lat, lon));
            }
            return results;
        }
        catch (Exception ex)
        {
            // Offline or Nominatim unreachable: the UI falls back to manual lat/lon entry.
            _logger.LogInformation(ex, "Geocode search for '{Query}' unavailable (offline?)", query);
            return Array.Empty<GeocodeResult>();
        }
    }

    public async Task<string?> ReverseAsync(double latitude, double longitude, CancellationToken ct)
    {
        try
        {
            var url = $"reverse?format=jsonv2&lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var place = await _http.GetFromJsonAsync<NominatimPlace>(url, Json, ct);
            return place?.DisplayName;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Reverse geocode ({Lat},{Lon}) unavailable (offline?)", latitude, longitude);
            return null;
        }
    }

    private static bool TryParse(string? s, out double value) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private sealed record NominatimPlace(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon);
}
