// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;
using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// OwnTracks-compatible geofencing ingest (roadmap Epic 3D — presence as a platform signal). OwnTracks
/// (privacy-first, self-hosted-friendly) POSTs the same JSON in both its HTTP and MQTT modes; this endpoint
/// accepts the HTTP-mode payload so no MQTT broker needs to be exposed. It normalizes an OwnTracks
/// <c>location</c>/<c>transition</c> message onto <see cref="PresenceReportedV1"/> and publishes it on the
/// bus; the AutomationService's presence layer resolves the resident and applies the server-side geofence.
///
/// <para>The POST is <see cref="AllowAnonymousAttribute">anonymous</see> (OwnTracks authenticates with HTTP
/// Basic / a URL, not our JWT). When a token is configured in the presence settings, it is required as
/// <c>?key=</c> or the <c>X-Presence-Token</c> header — set one whenever the endpoint is reachable off-LAN so
/// a stranger can't spoof presence. The response is an empty JSON array, which is what the OwnTracks HTTP
/// client expects (a list of friends' cards — we return none).</para>
/// </summary>
[ApiController]
[Route("api/presence")]
public class PresenceController : ControllerBase
{
    private const string HomeRegion = "home";

    private readonly IMessageBus _messageBus;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PresenceController> _logger;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PresenceController(IMessageBus messageBus, IHttpClientFactory httpClientFactory, ILogger<PresenceController> logger)
    {
        _messageBus = messageBus;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Live presence status (roadmap Epic 3D): the resident roster joined with each one's home/away
    /// + the occupancy aggregate. Proxied from the AutomationService, which holds the live signal.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("automation-service");
        try
        {
            using var upstream = await client.GetAsync("api/presence/status", HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await upstream.Content.ReadAsStringAsync(ct);
            return new ContentResult
            {
                StatusCode = (int)upstream.StatusCode,
                Content = string.IsNullOrEmpty(body) ? null : body,
                ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reach the AutomationService for presence status");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "presence status unavailable" });
        }
    }

    /// <summary>OwnTracks HTTP-mode endpoint: one location/transition message per POST. Optional <c>?user=</c>
    /// disambiguates the resident when the payload carries no usable <c>tid</c>/<c>topic</c>.</summary>
    [HttpPost("owntracks")]
    [AllowAnonymous]
    public async Task<IActionResult> OwnTracks([FromQuery] string? user, [FromQuery] string? key, CancellationToken ct)
    {
        var expectedToken = await GetIngestTokenAsync(ct);
        if (!string.IsNullOrEmpty(expectedToken))
        {
            var supplied = string.IsNullOrEmpty(key)
                ? Request.Headers["X-Presence-Token"].FirstOrDefault()
                : key;
            if (!string.Equals(supplied, expectedToken, StringComparison.Ordinal))
                return Unauthorized(new { error = "invalid presence ingest token" });
        }

        JsonElement root;
        try
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "body is not valid JSON" });
        }

        // OwnTracks only carries presence in `location` (position → geofence) and `transition` (enter/leave a
        // named region). Everything else (waypoints, lwt, cmd, …) is acknowledged and ignored.
        var type = Str(root, "_type");
        if (type is not ("location" or "transition"))
            return Ok(Array.Empty<object>());

        var keys = ResolveKeys(root, user);
        if (keys.Count == 0)
        {
            _logger.LogDebug("OwnTracks report with no resolvable resident key ignored");
            return Ok(Array.Empty<object>());
        }

        // A transition on the "home" region is a trusted enter/leave signal that bypasses the geofence math;
        // any other region still forwards coordinates so the server-side geofence can decide.
        bool? explicitPresent = null;
        if (type == "transition"
            && string.Equals(Str(root, "desc") ?? Str(root, "rid"), HomeRegion, StringComparison.OrdinalIgnoreCase))
        {
            explicitPresent = string.Equals(Str(root, "event"), "enter", StringComparison.OrdinalIgnoreCase);
        }

        var report = new PresenceReportedV1(
            ResidentKeys: keys,
            Latitude: Num(root, "lat"),
            Longitude: Num(root, "lon"),
            AccuracyMeters: Num(root, "acc"),
            BatteryPercent: (int?)Num(root, "batt"),
            ExplicitPresent: explicitPresent,
            ReportedAt: DateTimeOffset.UtcNow);

        var envelope = Envelope<PresenceReportedV1>.Create(
            MessageTypes.PresenceReported,
            source: "owntracks",
            data: report,
            subject: keys[0]);
        await _messageBus.PublishAsync(BusTopology.EventsExchange, BusTopology.PresenceReportedKey, envelope, ct);

        _logger.LogInformation("Presence report ingested for {Keys} ({Type})", string.Join("/", keys), type);
        return Ok(Array.Empty<object>());
    }

    /// <summary>The candidate keys a report may match a resident on: the OwnTracks tracker id, the topic's
    /// last segment (<c>owntracks/user/device</c> → <c>user</c>), the full topic, and the query override.</summary>
    private static List<string> ResolveKeys(JsonElement root, string? user)
    {
        var keys = new List<string>();
        void Add(string? v) { if (!string.IsNullOrWhiteSpace(v) && !keys.Contains(v!)) keys.Add(v!.Trim()); }

        Add(user);
        Add(Str(root, "tid"));
        var topic = Str(root, "topic");
        if (!string.IsNullOrWhiteSpace(topic))
        {
            Add(topic);
            var segments = topic!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2) Add(segments[^2]); // owntracks/<user>/<device> → <user>
        }
        return keys;
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    /// <summary>Read the configured ingest token from the presence settings (server-to-server); null/blank ⇒
    /// the endpoint is open. A gateway hiccup fails closed only if a token was expected — here we treat an
    /// unreachable settings read as "no token configured" to avoid locking out presence during an outage.</summary>
    private async Task<string?> GetIngestTokenAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("db-gateway");
            var settings = await client.GetFromJsonAsync<PresenceSettingsDto>("api/settings/presence", Json, ct);
            return string.IsNullOrWhiteSpace(settings?.OwnTracksToken) ? null : settings!.OwnTracksToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read presence ingest token; treating endpoint as open");
            return null;
        }
    }

    private sealed record PresenceSettingsDto(string? OwnTracksToken);
}
