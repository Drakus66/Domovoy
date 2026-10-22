// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Thread-safe holder of the current power-source signal (roadmap Epic 3C-LM) — which supply is feeding
/// the house (grid/grid_peak/battery/solar/off). Unlike <see cref="HomeModeState"/> this is <b>not</b>
/// backed by the DbGateway: it is a live signal (typically driven by a rule bridging a real inverter/ATS
/// device, or a manual override), not durable user configuration, so it resets to the safe default
/// (<see cref="WellKnownPowerSources.Default"/>) on restart rather than round-tripping through storage.
/// <see cref="SystemSensorService"/> writes it from commands aimed at the Power virtual device;
/// <see cref="LoadManager"/> reads it to pick the active budget tier.
/// </summary>
public sealed class PowerSourceState
{
    private volatile string _current = WellKnownPowerSources.Default;

    public string Current => _current;

    /// <summary>Replace the current signal (no-op for null/blank). Returns the effective value.</summary>
    public string Set(string? source)
    {
        if (!string.IsNullOrWhiteSpace(source))
            _current = source.Trim();
        return _current;
    }
}
