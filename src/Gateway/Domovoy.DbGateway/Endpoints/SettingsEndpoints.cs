// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.DbGateway.Services;

using GeoTimeZone;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Installation settings (roadmap Epic 2K). Today this owns the single site-location document
/// (<c>site_location</c>): the coordinates the AutomationService reads for sunrise/sunset geometry, plus a
/// display label and an IANA timezone. The location is edited from the WebUI Settings page and made
/// runtime-mutable so changing it needs no redeploy (it used to live only in appsettings).
///
/// Offline-first: the timezone is derived from the coordinates locally (GeoTimeZone), so it is correct with
/// no network. The geocode endpoint is the only networked part and is optional — it turns
/// a place name or map click into coordinates while editing; a manual lat/lon entry never needs it.
/// </summary>
public static class SettingsEndpoints
{
    public const string Collection = "site_location";
    public const string CalendarCollection = "calendar_settings";

    public record LocationUpdate(double Latitude, double Longitude, string? Label, string? TimeZoneId, bool TimeZoneAuto);
    public record CalendarUpdate(List<int>? WeekendDays, List<string>? Holidays);

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings").WithOpenApi();

        // Current site location (defaults to Moscow until the user sets one).
        group.MapGet("/location", async (IMongoDatabase db) => Results.Ok(await GetOrDefault(db)));

        // Set the site location. The timezone is derived from the coordinates unless the user pinned one.
        group.MapPut("/location", async (LocationUpdate body, IMongoDatabase db) =>
        {
            if (body.Latitude is < -90 or > 90)
                return Results.BadRequest(new { error = "latitude must be between -90 and 90" });
            if (body.Longitude is < -180 or > 180)
                return Results.BadRequest(new { error = "longitude must be between -180 and 180" });

            var timeZoneId = body.TimeZoneAuto || string.IsNullOrWhiteSpace(body.TimeZoneId)
                ? ResolveTimeZone(body.Latitude, body.Longitude)
                : body.TimeZoneId.Trim();

            var location = new SiteLocation
            {
                Latitude = body.Latitude,
                Longitude = body.Longitude,
                Label = string.IsNullOrWhiteSpace(body.Label) ? null : body.Label.Trim(),
                TimeZoneId = timeZoneId,
                TimeZoneAuto = body.TimeZoneAuto,
                UpdatedAt = DateTime.UtcNow,
            };

            await Locations(db).ReplaceOneAsync(
                x => x.Id == SiteLocation.SingletonId, location, new ReplaceOptions { IsUpsert = true });

            return Results.Ok(location);
        });

        // Resolve an IANA timezone from coordinates (offline) — lets the UI preview the auto tz before saving.
        group.MapGet("/timezone", (double lat, double lon) =>
            Results.Ok(new { timeZoneId = ResolveTimeZone(lat, lon) }));

        // Forward-geocode a place name → candidate coordinates (optional, online-only; empty when offline).
        group.MapGet("/geocode", async (string? q, IGeocoder geocoder, CancellationToken ct) =>
            Results.Ok(await geocoder.SearchAsync(q ?? string.Empty, ct)));

        // Reverse-geocode a map click → a display label (optional, online-only; null when offline).
        group.MapGet("/reverse-geocode", async (double lat, double lon, IGeocoder geocoder, CancellationToken ct) =>
            Results.Ok(new { label = await geocoder.ReverseAsync(lat, lon, ct) }));

        // --- Calendar sensor settings (roadmap Epic 2L) ---

        group.MapGet("/calendar", async (IMongoDatabase db) => Results.Ok(await GetCalendarOrDefault(db)));

        group.MapPut("/calendar", async (CalendarUpdate body, IMongoDatabase db) =>
        {
            var weekend = (body.WeekendDays ?? new List<int> { 6, 0 })
                .Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d).ToList();
            var holidays = (body.Holidays ?? new List<string>())
                .Where(IsIsoDate).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();

            var settings = new CalendarSettings
            {
                WeekendDays = weekend,
                Holidays = holidays,
                UpdatedAt = DateTime.UtcNow,
            };
            await Calendars(db).ReplaceOneAsync(
                x => x.Id == CalendarSettings.SingletonId, settings, new ReplaceOptions { IsUpsert = true });
            return Results.Ok(settings);
        });

        // Optional online import: merge a country's public holidays for a year into the list (online-only).
        group.MapPost("/calendar/import", async (string country, int? year, IHolidayImporter importer, IMongoDatabase db, CancellationToken ct) =>
        {
            var settings = await GetCalendarOrDefault(db);
            var imported = await importer.ImportAsync(country, year ?? DateTime.UtcNow.Year, ct);

            var merged = settings.Holidays.Concat(imported.Where(IsIsoDate))
                .Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
            settings.Holidays = merged;
            settings.UpdatedAt = DateTime.UtcNow;

            await Calendars(db).ReplaceOneAsync(
                x => x.Id == CalendarSettings.SingletonId, settings, new ReplaceOptions { IsUpsert = true });
            return Results.Ok(new { imported = imported.Count, settings });
        });
    }

    /// <summary>Derive the IANA timezone id for a coordinate, fully offline (GeoTimeZone's embedded shapes).</summary>
    private static string ResolveTimeZone(double lat, double lon) =>
        TimeZoneLookup.GetTimeZone(lat, lon).Result;

    private static async Task<SiteLocation> GetOrDefault(IMongoDatabase db)
    {
        var location = await Locations(db).Find(x => x.Id == SiteLocation.SingletonId).FirstOrDefaultAsync();
        return location ?? new SiteLocation { TimeZoneId = ResolveTimeZone(55.7558, 37.6173) };
    }

    private static async Task<CalendarSettings> GetCalendarOrDefault(IMongoDatabase db)
    {
        var settings = await Calendars(db).Find(x => x.Id == CalendarSettings.SingletonId).FirstOrDefaultAsync();
        return settings ?? new CalendarSettings();
    }

    /// <summary>Strict <c>yyyy-MM-dd</c> check so a bad string can never poison the holiday list.</summary>
    private static bool IsIsoDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    private static IMongoCollection<SiteLocation> Locations(IMongoDatabase db) =>
        db.GetCollection<SiteLocation>(Collection);

    private static IMongoCollection<CalendarSettings> Calendars(IMongoDatabase db) =>
        db.GetCollection<CalendarSettings>(CalendarCollection);
}
