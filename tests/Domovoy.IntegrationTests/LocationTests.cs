using System.Net;
using System.Text;

using Domovoy.AutomationService.Services;
using Domovoy.DbGateway.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the runtime-location feature (roadmap Epic 2K): the sun calculator being repointable at a
/// new site, and the geocoder degrading to an empty result offline. Pure — no infrastructure.
/// </summary>
public sealed class LocationTests
{
    // --- SunCalculator is runtime-mutable (the location is now editable from the WebUI) ----------

    [Fact]
    public void SunCalculator_Update_RepointsCoordinates()
    {
        var sun = new SunCalculator(55.7558, 37.6173); // Moscow
        sun.Update(-33.8688, 151.2093);                // Sydney

        Assert.Equal(-33.8688, sun.Latitude, 4);
        Assert.Equal(151.2093, sun.Longitude, 4);
    }

    [Fact]
    public void SunCalculator_SolarNoon_MatchesLongitudeInUtc()
    {
        // Absolute-time guard: solar noon (the midpoint of sunrise→sunset) in UTC is fixed by longitude —
        // the sun crosses the meridian ~4 min/degree east before Greenwich, so Moscow's ~37.6°E noon lands
        // near 12:00 − 37.6·4 min ≈ 09:29 UTC (±~a few min for the equation of time). A flipped longitude
        // sign pushed noon to ~14:30 UTC, producing a sunrise inconsistent with the site timezone. This
        // pins the direction so that can't regress; the other tests only check relative daylight length.
        var sun = new SunCalculator(55.7558, 37.6173); // Moscow, 37.6°E
        var (rise, set) = sun.ForDate(new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(rise);
        Assert.NotNull(set);

        var solarNoonUtc = rise!.Value + (set!.Value - rise.Value) / 2;
        var expectedNoonUtc = new DateTime(2026, 6, 21, 9, 29, 0, DateTimeKind.Utc);
        Assert.True(Math.Abs((solarNoonUtc - expectedNoonUtc).TotalMinutes) < 15,
            $"Solar noon {solarNoonUtc:HH:mm} UTC should sit near {expectedNoonUtc:HH:mm} UTC for 37.6°E");
    }

    [Fact]
    public void SunCalculator_Update_ChangesSunriseSunset()
    {
        var day = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc); // northern summer solstice
        var sun = new SunCalculator(55.7558, 37.6173);                  // Moscow: long day
        var (moscowRise, moscowSet) = sun.ForDate(day);

        sun.Update(-33.8688, 151.2093);                                 // Sydney: short day (their winter)
        var (sydneyRise, sydneySet) = sun.ForDate(day);

        Assert.NotNull(moscowRise);
        Assert.NotNull(sydneyRise);
        // Repointing must actually move the geometry — daylight length differs markedly between the sites.
        var moscowDaylight = (moscowSet!.Value - moscowRise!.Value).TotalHours;
        var sydneyDaylight = (sydneySet!.Value - sydneyRise!.Value).TotalHours;
        Assert.True(moscowDaylight > sydneyDaylight + 5,
            $"Moscow daylight {moscowDaylight:F1}h should far exceed Sydney {sydneyDaylight:F1}h at the June solstice");
    }

    // --- Geocoder degrades gracefully offline (offline-first) ------------------------------------

    [Fact]
    public async Task Geocoder_Offline_ReturnsEmpty()
    {
        var geocoder = new NominatimGeocoder(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://nominatim.example/") },
            NullLogger<NominatimGeocoder>.Instance);

        var results = await geocoder.SearchAsync("Berlin", CancellationToken.None);

        Assert.Empty(results); // a network failure must not surface as an error in the settings UI
    }

    [Fact]
    public async Task Geocoder_ParsesResults()
    {
        const string json = """
            [{"display_name":"Berlin, Germany","lat":"52.5200","lon":"13.4050"}]
            """;
        var geocoder = new NominatimGeocoder(
            new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("https://nominatim.example/") },
            NullLogger<NominatimGeocoder>.Instance);

        var results = await geocoder.SearchAsync("Berlin", CancellationToken.None);

        var hit = Assert.Single(results);
        Assert.Equal("Berlin, Germany", hit.Label);
        Assert.Equal(52.52, hit.Latitude, 3);
        Assert.Equal(13.405, hit.Longitude, 3);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("offline");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }
}
