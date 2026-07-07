// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Publishes the platform's own <b>virtual sensors</b> (roadmap Epic 2L) as first-class capability devices —
/// things the system knows without any hardware: right now the <b>Sun</b> (elevation, azimuth, is-dark/is-day,
/// today's sunrise/sunset). It announces each once via <see cref="DeviceDiscoveredV1"/> and republishes state
/// every minute via <see cref="DeviceStateReportV1"/>, exactly like an adapter — so the existing pipeline turns
/// them into rows in <c>capability_devices</c> (dashboard), live values in the rule engine (<c>DeviceState</c>
/// triggers/conditions), and history. <c>AdapterSource="System"</c> marks them virtual. Time and calendar
/// sensors follow the same pattern in later increments.
/// </summary>
public sealed class SystemSensorService : BackgroundService
{
    /// <summary>Adapter-source tag identifying platform-published virtual sensors.</summary>
    public const string Source = "System";

    private static readonly Guid SunDeviceId = DeviceIdFactory.Derive(Source, "sun");
    private static readonly Guid TimeDeviceId = DeviceIdFactory.Derive(Source, "time");
    private static readonly Guid CalendarDeviceId = DeviceIdFactory.Derive(Source, "calendar");
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static readonly Capability[] SunCapabilities =
    {
        WellKnownCapabilities.SunElevation(),
        WellKnownCapabilities.SunAzimuth(),
        WellKnownCapabilities.IsDark(),
        WellKnownCapabilities.IsDay(),
        WellKnownCapabilities.Sunrise(),
        WellKnownCapabilities.Sunset(),
    };

    private static readonly Capability[] TimeCapabilities =
    {
        WellKnownCapabilities.TimeOfDay(),
        WellKnownCapabilities.Clock(),
    };

    private static readonly Capability[] CalendarCapabilities =
    {
        WellKnownCapabilities.DayOfWeek(),
        WellKnownCapabilities.IsWeekend(),
        WellKnownCapabilities.IsHoliday(),
        WellKnownCapabilities.CalendarDate(),
    };

    private readonly IMessageBus _bus;
    private readonly SunCalculator _sun;
    private readonly SiteContext _site;
    private readonly CalendarContext _calendar;
    private readonly ILogger<SystemSensorService> _logger;

    public SystemSensorService(
        IMessageBus bus, SunCalculator sun, SiteContext site, CalendarContext calendar,
        ILogger<SystemSensorService> logger)
    {
        _bus = bus;
        _sun = sun;
        _site = site;
        _calendar = calendar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await AnnounceSunAsync(stoppingToken);
        await AnnounceAsync(TimeDeviceId, "Time", "system/clock", TimeCapabilities, stoppingToken);
        await AnnounceAsync(CalendarDeviceId, "Calendar", "system/calendar", CalendarCapabilities, stoppingToken);
        _logger.LogInformation("SystemSensorService started (tick {Seconds}s)", TickInterval.TotalSeconds);

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                await PublishSunAsync(now, stoppingToken);
                await PublishStateAsync(TimeDeviceId, "time", ComputeTime(now).ToState(), stoppingToken);
                await PublishStateAsync(CalendarDeviceId, "calendar", ComputeCalendar(now).ToState(), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "System sensor tick failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    /// <summary>Local wall-clock now at the site (2L) — the basis for the Time and Calendar sensors.</summary>
    private DateTimeOffset LocalNow(DateTimeOffset nowUtc) => TimeZoneInfo.ConvertTime(nowUtc, _site.TimeZone);

    /// <summary>Time sensor snapshot: minutes since local midnight + a HH:mm clock. Pure/testable.</summary>
    public TimeSnapshot ComputeTime(DateTimeOffset nowUtc)
    {
        var local = LocalNow(nowUtc);
        return new TimeSnapshot(local.Hour * 60 + local.Minute, local.ToString("HH:mm"));
    }

    /// <summary>Calendar sensor snapshot: local day-of-week, weekend/holiday flags, date. Pure/testable.</summary>
    public CalendarSnapshot ComputeCalendar(DateTimeOffset nowUtc)
    {
        var local = LocalNow(nowUtc);
        var date = DateOnly.FromDateTime(local.DateTime);
        return new CalendarSnapshot(
            DayOfWeek: local.DayOfWeek.ToString(),
            IsWeekend: _calendar.IsWeekend(local.DayOfWeek),
            IsHoliday: _calendar.IsHoliday(date),
            Date: date.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Compute the Sun sensor's capability values for an instant. Pure (no bus/DI) so it is unit-testable;
    /// numeric angles are rounded to 0.1° to keep telemetry from churning on sub-degree jitter.
    /// </summary>
    public SunSnapshot ComputeSun(DateTimeOffset nowUtc)
    {
        var (elevation, azimuth) = _sun.Position(nowUtc);
        var (sunrise, sunset) = _sun.ForDate(nowUtc.UtcDateTime);
        return new SunSnapshot(
            Elevation: Math.Round(elevation, 1),
            Azimuth: Math.Round(azimuth, 1),
            IsDark: _sun.IsDark(nowUtc),
            IsDay: elevation > 0,
            Sunrise: FormatLocal(sunrise),
            Sunset: FormatLocal(sunset));
    }

    private string FormatLocal(DateTime? utc)
    {
        if (utc is null) return "—"; // polar day/night
        var asUtc = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, _site.TimeZone).ToString("HH:mm");
    }

    private Task PublishSunAsync(DateTimeOffset nowUtc, CancellationToken ct) =>
        PublishStateAsync(SunDeviceId, "sun", ComputeSun(nowUtc).ToState(), ct);

    private Task AnnounceSunAsync(CancellationToken ct) =>
        AnnounceAsync(SunDeviceId, "Sun", "system/sun", SunCapabilities, ct);

    private async Task PublishStateAsync(Guid deviceId, string tag, IReadOnlyDictionary<string, object?> state, CancellationToken ct)
    {
        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"{Source.ToLowerInvariant()}:{tag}",
            data: new DeviceStateReportV1(deviceId, state),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);
    }

    private async Task AnnounceAsync(Guid deviceId, string name, string model, Capability[] capabilities, CancellationToken ct)
    {
        var hardwareId = model.StartsWith("system/", StringComparison.Ordinal) ? model["system/".Length..] : name.ToLowerInvariant();
        var descriptor = new DeviceDescriptor(
            Id: deviceId,
            Name: name,
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(Source, hardwareId),
            Capabilities: capabilities,
            Manufacturer: "Domovoy",
            Model: model);

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"{Source.ToLowerInvariant()}:{hardwareId}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope, ct);
        _logger.LogInformation("Announced system {Name} sensor as device {DeviceId}", name, deviceId);
    }

    /// <summary>Computed Sun-sensor reading; <see cref="ToState"/> maps it onto capability ids for the bus.</summary>
    public sealed record SunSnapshot(
        double Elevation, double Azimuth, bool IsDark, bool IsDay, string Sunrise, string Sunset)
    {
        public IReadOnlyDictionary<string, object?> ToState() => new Dictionary<string, object?>
        {
            [CapabilityIds.SunElevation] = Elevation,
            [CapabilityIds.SunAzimuth] = Azimuth,
            [CapabilityIds.IsDark] = IsDark,
            [CapabilityIds.IsDay] = IsDay,
            [CapabilityIds.Sunrise] = Sunrise,
            [CapabilityIds.Sunset] = Sunset,
        };
    }

    /// <summary>Computed Time-sensor reading (local wall clock).</summary>
    public sealed record TimeSnapshot(int TimeOfDay, string Clock)
    {
        public IReadOnlyDictionary<string, object?> ToState() => new Dictionary<string, object?>
        {
            [CapabilityIds.TimeOfDay] = TimeOfDay,
            [CapabilityIds.Clock] = Clock,
        };
    }

    /// <summary>Computed Calendar-sensor reading (local date context).</summary>
    public sealed record CalendarSnapshot(string DayOfWeek, bool IsWeekend, bool IsHoliday, string Date)
    {
        public IReadOnlyDictionary<string, object?> ToState() => new Dictionary<string, object?>
        {
            [CapabilityIds.DayOfWeek] = DayOfWeek,
            [CapabilityIds.IsWeekend] = IsWeekend,
            [CapabilityIds.IsHoliday] = IsHoliday,
            [CapabilityIds.CalendarDate] = Date,
        };
    }
}
