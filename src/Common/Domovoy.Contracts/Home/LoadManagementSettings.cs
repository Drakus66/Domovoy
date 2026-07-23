// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// Load-shedding configuration (roadmap Epic 3C-LM). A single persisted document the DbGateway owns and
/// the AutomationService's <c>LoadManager</c> reads. Off by default — an empty/disabled document is a
/// no-op, matching the household-safety invariant that this coordinator only ever adds a soft interlock
/// the owner explicitly opted into.
/// </summary>
public class LoadManagementSettings
{
    /// <summary>There is only ever one load-management document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>Master switch — false ⇒ LoadManager never sheds/curtails anything regardless of budgets.</summary>
    public bool Enabled { get; set; }

    /// <summary>Power budgets by current <see cref="WellKnownPowerSources"/> signal. No entry for the
    /// active source ⇒ unlimited (no shedding) for that source.</summary>
    public List<PowerBudget> Budgets { get; set; } = new();

    /// <summary>Per-phase limits (W) for a polyphase intake (Epic 3C-D). A three-phase house trips its
    /// per-phase breaker long before the household total is reached, so these are checked independently of
    /// <see cref="Budgets"/>. No entry ⇒ the phase falls back to its breaker rating from the topology, and
    /// without one it is unlimited.</summary>
    public List<PhaseLimit> PhaseLimits { get; set; } = new();

    /// <summary>Per-circuit overrides (W) of the breaker rating in the topology (Epic 3C-D). Rarely needed —
    /// a rated breaker already yields a limit; this exists for a line the owner wants to keep below its rating.</summary>
    public List<CircuitLimit> CircuitLimits { get; set; } = new();

    /// <summary>Headroom (W) required below the budget before a shed load is restored — avoids flapping
    /// right at the limit.</summary>
    public double RestoreMarginWatts { get; set; } = 100;

    /// <summary>Minimum time a shed/restore action must hold before it can be reversed.</summary>
    public int MinDwellSeconds { get; set; } = 120;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A power budget (W) that applies while the house's <c>power_source</c> signal equals <see cref="PowerSource"/>.</summary>
public class PowerBudget
{
    public string PowerSource { get; set; } = WellKnownPowerSources.Default;

    public double LimitWatts { get; set; }
}

/// <summary>A limit (W) on one phase of the intake (<c>l1</c>/<c>l2</c>/<c>l3</c>), Epic 3C-D.</summary>
public class PhaseLimit
{
    public string Phase { get; set; } = string.Empty;

    public double LimitWatts { get; set; }
}

/// <summary>A limit (W) on one circuit of the topology, overriding its breaker rating (Epic 3C-D).</summary>
public class CircuitLimit
{
    public string CircuitId { get; set; } = string.Empty;

    public double LimitWatts { get; set; }
}

/// <summary>
/// Well-known values for the <c>power_source</c> capability (roadmap Epic 3C-LM) — which supply is
/// currently feeding the house. Modelled as an open set of strings (like <see cref="WellKnownModes"/>),
/// so a deployment-specific inverter/ATS integration can report a custom value. Matched case-insensitively.
/// </summary>
public static class WellKnownPowerSources
{
    /// <summary>Normal utility grid supply.</summary>
    public const string Grid = "grid";

    /// <summary>Grid supply during a peak-demand/high-price window — its own budget tier.</summary>
    public const string GridPeak = "grid_peak";

    /// <summary>Running off a battery/UPS (grid absent or bypassed).</summary>
    public const string Battery = "battery";

    /// <summary>Running off on-site solar generation.</summary>
    public const string Solar = "solar";

    /// <summary>No supply signal available / power is out.</summary>
    public const string Off = "off";

    /// <summary>The default signal when nothing has ever commanded the Power virtual device.</summary>
    public const string Default = Grid;

    /// <summary>Canonical ordering for UI; deployments may report additional custom sources too.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Grid, GridPeak, Battery, Solar, Off };
}
