// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml;
using Domovoy.AutomationService.Services.Notifications;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Periodically reloads user rules and the device read-model (zone + initial state) from the DbGateway.
/// Keeps the engine's view fresh without each rule edit needing a bus round-trip (roadmap Epic 1A; a
/// push-invalidation event is a later optimization).
/// </summary>
public sealed class RefreshLoop : BackgroundService
{
    private readonly RuleStore _store;
    private readonly SceneStore _scenes;
    private readonly BlockStore _blocks;
    private readonly VariableStore _variables;
    private readonly DeviceRegistry _registry;
    private readonly DbGatewayClient _db;
    private readonly HomeModeState _mode;
    private readonly SunCalculator _sun;
    private readonly SiteContext _site;
    private readonly CalendarContext _calendar;
    private readonly TariffContext _tariff;
    private readonly LoadManager _loadManager;
    private readonly DeviceEnergyService _deviceEnergy;
    private readonly MlRuntimeState _mlRuntime;
    private readonly NotificationRuntimeState _notifications;
    private readonly AutomationOptions _options;
    private readonly ILogger<RefreshLoop> _logger;

    public RefreshLoop(
        RuleStore store, SceneStore scenes, BlockStore blocks, VariableStore variables, DeviceRegistry registry, DbGatewayClient db, HomeModeState mode,
        SunCalculator sun, SiteContext site, CalendarContext calendar, TariffContext tariff, LoadManager loadManager,
        DeviceEnergyService deviceEnergy, MlRuntimeState mlRuntime, NotificationRuntimeState notifications, IOptions<AutomationOptions> options,
        ILogger<RefreshLoop> logger)
    {
        _store = store;
        _scenes = scenes;
        _blocks = blocks;
        _variables = variables;
        _registry = registry;
        _db = db;
        _mode = mode;
        _sun = sun;
        _site = site;
        _calendar = calendar;
        _tariff = tariff;
        _loadManager = loadManager;
        _deviceEnergy = deviceEnergy;
        _mlRuntime = mlRuntime;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(5, _options.RefreshSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            await _store.RefreshAsync(stoppingToken);
            await _scenes.RefreshAsync(stoppingToken);
            await _blocks.RefreshAsync(stoppingToken);
            await _variables.RefreshAsync(stoppingToken);
            var devices = await RefreshDevices(stoppingToken);
            await RefreshMode(stoppingToken);
            await RefreshLocation(stoppingToken);
            await RefreshCalendar(stoppingToken);
            await RefreshTariff(stoppingToken);
            await RefreshLoadManagement(devices, stoppingToken);
            if (devices is not null) _deviceEnergy.Sync(devices); // 3C-D: which devices the estimator maintains
            await RefreshMlSettings(stoppingToken);
            await RefreshNotificationSettings(stoppingToken);

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Returns the fetched devices (or null if the gateway was unreachable) so <see cref="RefreshLoadManagement"/>
    /// can reuse the same read without a second HTTP round-trip.</summary>
    private async Task<List<DbGatewayClient.DeviceSnapshot>?> RefreshDevices(CancellationToken ct)
    {
        var devices = await _db.GetDevicesAsync(ct);
        if (devices is null) return null;

        foreach (var d in devices)
        {
            if (!Guid.TryParse(d.Id, out var id)) continue;
            _registry.SetZone(id, d.ZoneId);
            foreach (var kv in d.State)
                _registry.SeedValue(id, kv.Key, kv.Value);
        }

        _logger.LogDebug("Refreshed {Count} device(s) from read-model", devices.Count);
        return devices;
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

    // Refresh the tariff config (3C) feeding the tariff virtual device and the cheap-hours block. Gateway
    // unreachable → keep the last-known tariff (offline-first).
    private async Task RefreshTariff(CancellationToken ct)
    {
        var settings = await _db.GetTariffSettingsAsync(ct);
        if (settings is not null) _tariff.Update(settings);
    }

    // Refresh LoadManager's tracked devices + budgets (3C-LM). Reuses the devices list RefreshDevices
    // already fetched this cycle; a null devices list (gateway unreachable) is skipped so LoadManager
    // keeps its last-known metadata (offline-first) rather than losing every managed load.
    private async Task RefreshLoadManagement(List<DbGatewayClient.DeviceSnapshot>? devices, CancellationToken ct)
    {
        if (devices is null) return;
        var settings = await _db.GetLoadManagementSettingsAsync(ct);
        // The electrical topology (3C-D) adds per-phase/per-circuit limits; null (unreachable) keeps the
        // last-known tree rather than silently collapsing back to a single household budget.
        var topology = await _db.GetPowerTopologyAsync(ct);
        _loadManager.Sync(devices, settings, topology);
    }

    // Refresh the intelligence-layer switches (3I) so toggling the layer/proposers from the UI takes effect
    // within one cycle. Gateway unreachable → keep the last-known settings (offline-first: an outage must not
    // silently disable ML).
    private async Task RefreshMlSettings(CancellationToken ct)
    {
        var settings = await _db.GetMlSettingsAsync(ct);
        _mlRuntime.Set(settings);
    }

    // Refresh the notification-discipline settings (3F) so muting a channel or changing a rate-limit from the UI
    // takes effect within one cycle. Gateway unreachable → keep the last-known settings (offline-first: an outage
    // must not silently change what notifications get delivered).
    private async Task RefreshNotificationSettings(CancellationToken ct)
    {
        var settings = await _db.GetNotificationSettingsAsync(ct);
        _notifications.Set(settings);
    }
}
