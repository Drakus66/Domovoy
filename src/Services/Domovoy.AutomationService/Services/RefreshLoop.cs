// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Periodically reloads user rules and the device read-model (zone + initial state) from the DbGateway,
/// and loads the local safety floor once at startup. Keeps the engine's view fresh without each rule
/// edit needing a bus round-trip (roadmap Epic 1A; a push-invalidation event is a later optimization).
/// </summary>
public sealed class RefreshLoop : BackgroundService
{
    private readonly RuleStore _store;
    private readonly BlockStore _blocks;
    private readonly DeviceRegistry _registry;
    private readonly DbGatewayClient _db;
    private readonly HomeModeState _mode;
    private readonly SunCalculator _sun;
    private readonly SiteContext _site;
    private readonly CalendarContext _calendar;
    private readonly AutomationOptions _options;
    private readonly ILogger<RefreshLoop> _logger;

    public RefreshLoop(
        RuleStore store, BlockStore blocks, DeviceRegistry registry, DbGatewayClient db, HomeModeState mode,
        SunCalculator sun, SiteContext site, CalendarContext calendar,
        IOptions<AutomationOptions> options, ILogger<RefreshLoop> logger)
    {
        _store = store;
        _blocks = blocks;
        _registry = registry;
        _db = db;
        _mode = mode;
        _sun = sun;
        _site = site;
        _calendar = calendar;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _store.LoadSafetyRules();

        var period = TimeSpan.FromSeconds(Math.Max(5, _options.RefreshSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            await _store.RefreshAsync(stoppingToken);
            await _blocks.RefreshAsync(stoppingToken);
            await RefreshDevices(stoppingToken);
            await RefreshMode(stoppingToken);
            await RefreshLocation(stoppingToken);
            await RefreshCalendar(stoppingToken);

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RefreshDevices(CancellationToken ct)
    {
        var devices = await _db.GetDevicesAsync(ct);
        if (devices is null) return;

        foreach (var d in devices)
        {
            if (!Guid.TryParse(d.Id, out var id)) continue;
            _registry.SetZone(id, d.ZoneId);
            foreach (var kv in d.State)
                _registry.SeedValue(id, kv.Key, kv.Value);
        }

        _logger.LogDebug("Refreshed {Count} device(s) from read-model", devices.Count);
    }

    // Seed/repair the home mode from the DbGateway (1G). HomeModeMonitor keeps it live via the bus; this
    // also covers startup and any missed events. The gateway is the authority, so re-reading is consistent.
    private async Task RefreshMode(CancellationToken ct)
    {
        var mode = await _db.GetModeAsync(ct);
        if (mode is not null) _mode.Set(mode);
    }

    // Repoint the sunrise/sunset calculator at the persisted site location (2K). The location is now edited
    // from the WebUI (runtime-mutable), so this picks up a change without a redeploy. If the gateway is
    // unreachable the calculator keeps its last-known (or appsettings-seeded) coordinates — offline-first.
    private async Task RefreshLocation(CancellationToken ct)
    {
        var location = await _db.GetLocationAsync(ct);
        if (location is null) return;

        if (Math.Abs(location.Latitude - _sun.Latitude) > 1e-9 ||
            Math.Abs(location.Longitude - _sun.Longitude) > 1e-9)
        {
            _sun.Update(location.Latitude, location.Longitude);
            _logger.LogInformation(
                "Site location updated to {Lat},{Lon} ({Label})",
                location.Latitude, location.Longitude, location.Label ?? "unnamed");
        }

        // Timezone for local sunrise/sunset rendering on the system Sun sensor (2L).
        if (_site.SetTimeZone(location.TimeZoneId))
            _logger.LogInformation("Site timezone set to {TimeZone}", _site.TimeZone.Id);
    }

    // Refresh the calendar config (2L) feeding the system Calendar sensor. Gateway unreachable → keep the
    // last-known weekend/holiday set (offline-first).
    private async Task RefreshCalendar(CancellationToken ct)
    {
        var settings = await _db.GetCalendarSettingsAsync(ct);
        if (settings is not null) _calendar.Update(settings.WeekendDays, settings.Holidays);
    }
}
