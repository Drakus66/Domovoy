// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.CommutePlugin;

/// <summary>
/// Plugin configuration, bound from the <c>COMMUTE</c> section / environment (e.g.
/// <c>COMMUTE__SETTINGSBASEURL</c>). The supervisor does not inject env into plugins, so these are
/// supplied on the <c>plugin-supervisor</c> service and inherited by the child process (see
/// docker-compose). All values have working defaults so the plugin runs with zero config.
/// <para>
/// What is <b>not</b> here: the traffic backend and its API key. Those are plugin <b>settings</b>
/// (<see cref="CommuteSettings"/>), edited from the plugins panel and applied live over the settings
/// channel — an env variable would mean editing compose and restarting to change a provider.
/// </para>
/// </summary>
public sealed class CommuteOptions
{
    public const string SectionName = "Commute";

    /// <summary>Base URL of the public API gateway the plugin reads the site location + geocoder from (Epic 2K).</summary>
    public string SettingsBaseUrl { get; set; } = "http://api-gateway:8080";

    /// <summary>Average effective road speed (km/h) for the simulated provider.</summary>
    public double AvgSpeedKmh { get; set; } = 45;

    /// <summary>Fixed per-trip overhead (parking, walking) added by the simulated provider, minutes.</summary>
    public double FixedOverheadMinutes { get; set; } = 3;

    /// <summary>Recompute cadence when idle (no active goal), seconds.</summary>
    public int IdleIntervalSeconds { get; set; } = 900;

    /// <summary>Floor for the adaptive recompute interval, seconds (protects the traffic API budget).</summary>
    public int MinIntervalSeconds { get; set; } = 30;

    /// <summary>How often the cached site location is refreshed from the gateway, seconds.</summary>
    public int LocationRefreshSeconds { get; set; } = 600;
}
