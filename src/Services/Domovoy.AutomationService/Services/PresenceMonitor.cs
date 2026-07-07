// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Home;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Presence-driven home-mode switching (roadmap Epic 1G). Watches presence/occupancy capabilities on the
/// bus and switches the mode between Home and Away: any presence signal going true brings the home out of
/// Away immediately; once <i>all</i> presence signals have been clear for <see cref="AutomationOptions.AwayDelaySeconds"/>
/// it switches Home → Away. It deliberately never overrides a manual <c>Night</c>/<c>Vacation</c>
/// (<see cref="WellKnownModes.IsPresenceManaged"/>) — the occupant's explicit choice wins. Switches go
/// through the DbGateway (the persistence authority), which publishes the change everyone else consumes.
/// </summary>
public sealed class PresenceMonitor : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly HomeModeState _mode;
    private readonly DbGatewayClient _db;
    private readonly AutomationOptions _options;
    private readonly ILogger<PresenceMonitor> _logger;

    /// <summary>Last observed presence value per device — to know if anyone is currently present.</summary>
    private readonly ConcurrentDictionary<Guid, bool> _present = new();

    /// <summary>Last time any presence signal was true (UTC). Seeded to "now" so we don't flip to Away on boot.</summary>
    private DateTimeOffset _lastPresenceAt = DateTimeOffset.UtcNow;

    /// <summary>Debounce so a burst of reports during a switch doesn't spam the gateway.</summary>
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan RequestDebounce = TimeSpan.FromSeconds(5);

    public PresenceMonitor(
        IMessageBus bus, HomeModeState mode, DbGatewayClient db,
        IOptions<AutomationOptions> options, ILogger<PresenceMonitor> logger)
    {
        _bus = bus;
        _mode = mode;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PresenceAutoMode)
        {
            _logger.LogInformation("PresenceMonitor disabled (PresenceAutoMode=false) — mode stays manual");
            return;
        }

        await _bus.SubscribeAsync<Envelope<DeviceStateReportV1>>(
            "automation-presence",
            BusTopology.StateExchange,
            BusTopology.DeviceStateUpdatedKey,
            env => HandleStateReport(env, stoppingToken),
            stoppingToken);

        _logger.LogInformation("PresenceMonitor watching {Caps} (Away after {Delay}s clear)",
            string.Join("/", _options.PresenceCapabilities), _options.AwayDelaySeconds);

        // Periodic check for the Home → Away transition (no event fires when presence simply stays clear).
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await MaybeSwitchAway(stoppingToken);
    }

    private async Task HandleStateReport(Envelope<DeviceStateReportV1> envelope, CancellationToken ct)
    {
        var report = envelope.Data;
        if (report is null || report.State.Count == 0) return;

        var sawPresenceCap = false;
        var present = false;

        foreach (var kv in report.State)
        {
            if (!IsPresenceCapability(kv.Key)) continue;
            sawPresenceCap = true;
            var value = ValueOps.AsBool(kv.Value);
            _present[report.DeviceId] = value;
            if (value) present = true;
        }

        if (!sawPresenceCap) return;

        if (present)
        {
            _lastPresenceAt = DateTimeOffset.UtcNow;
            // Coming back: leave Away as soon as anyone is detected.
            if (string.Equals(_mode.Current, WellKnownModes.Away, StringComparison.OrdinalIgnoreCase))
                await RequestMode(WellKnownModes.Home, ct);
        }
    }

    private async Task MaybeSwitchAway(CancellationToken ct)
    {
        // Only manage Home ⇄ Away; never override a manual Night/Vacation.
        if (!string.Equals(_mode.Current, WellKnownModes.Home, StringComparison.OrdinalIgnoreCase)) return;
        if (AnyonePresent()) return;
        if (DateTimeOffset.UtcNow - _lastPresenceAt < TimeSpan.FromSeconds(_options.AwayDelaySeconds)) return;

        await RequestMode(WellKnownModes.Away, ct);
    }

    private bool AnyonePresent() => _present.Values.Any(v => v);

    private bool IsPresenceCapability(string capabilityId) =>
        _options.PresenceCapabilities.Any(c => string.Equals(c, capabilityId, StringComparison.OrdinalIgnoreCase));

    private async Task RequestMode(string mode, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRequestAt < RequestDebounce) return;
        _lastRequestAt = now;

        _logger.LogInformation("Presence → switching home mode to {Mode}", mode);
        await _db.SetModeAsync(mode, ModeChangeSources.Presence, ct);
    }
}
