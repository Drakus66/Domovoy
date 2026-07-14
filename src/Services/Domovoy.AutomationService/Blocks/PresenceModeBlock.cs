// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;

namespace Domovoy.AutomationService.Blocks;

using static GenericBlockConventions;

/// <summary>
/// Presence → home-mode governor as a <b>user-owned block</b>, replacing the hardcoded PresenceMonitor
/// service (Epic 1G): whether a home switches to Away when presence sensors go silent is household
/// policy, not a platform truth — homes without presence sensors simply never create this block.
///
/// <para>Any bound presence input true → propose the "home" mode immediately; all inputs false for
/// <c>awayDelayMin</c> → propose the "away" mode. The proposal is emitted on <c>home_mode</c>, which the
/// instance binds to the <b>Home virtual device</b>'s writable <c>home_mode</c> capability (the
/// device→mode bridge in SystemSensorService) — actuation, dedup, journal run-records and "by block"
/// attribution all come from the standard block pipeline.</para>
///
/// <para>The optional <c>current_mode</c> input (bind it to the same Home device) is the manual-mode
/// guard: when the current mode is neither the home nor the away mode (Night, Vacation, a custom mode),
/// the block emits nothing — presence must never override an occupant's explicit choice. Unbound, the
/// guard is off. The silence timer seeds to "now" on the first tick so a restart never flips the home
/// to Away by itself; <c>lastPresenceAt</c> lives in the persisted state bag (2Q block_state).</para>
/// </summary>
public sealed class PresenceModeType : IBlockType
{
    public string TypeId => "presence_mode";
    public string Category => BlockCategories.Template;
    public string Title => "Presence → home mode";
    public string Description =>
        "Switches the home mode by presence: any sensor true → home, all silent for a delay → away. " +
        "Bind the output to the Home device; never overrides manual modes (Night/Vacation).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("presence_a", CapabilityKind.Boolean, "Presence/occupancy sensor"),
        new BlockPortSpec("presence_b", CapabilityKind.Boolean, "Presence/occupancy sensor", Optional: true),
        new BlockPortSpec("presence_c", CapabilityKind.Boolean, "Presence/occupancy sensor", Optional: true),
        new BlockPortSpec("presence_d", CapabilityKind.Boolean, "Presence/occupancy sensor", Optional: true),
        new BlockPortSpec("current_mode", CapabilityKind.Enum,
            "Current home mode (bind to the Home device) — the manual-mode guard", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Enum(CapabilityIds.HomeMode, WellKnownModes.All, writable: false),
        WellKnownCapabilities.Boolean("presence_any", writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("awayDelayMin", 10, "min", 1, null, "Silence before switching to the away mode"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("homeMode", BlockOptionKind.Text, WellKnownModes.Home, "Mode when presence is detected"),
        new BlockOptionSpec("awayMode", BlockOptionKind.Text, WellKnownModes.Away, "Mode after the silence delay"),
    };

    public IBlock Create() => new PresenceModeBlock();
}

public sealed class PresenceModeBlock : IBlock
{
    private static readonly string[] PresencePorts = { "presence_a", "presence_b", "presence_c", "presence_d" };

    public void Tick(IBlockContext ctx)
    {
        // Any bound presence input true → someone is home. All ports unbound/unknown → do nothing at all.
        var readings = PresencePorts.Select(p => ReadBool(ctx, p)).Where(v => v is not null).ToList();
        if (readings.Count == 0) return;
        var present = readings.Any(v => v == true);

        // Seed the silence timer on the first tick so a (re)start never flips the home to Away by itself.
        var lastPresenceAt = ctx.GetState<DateTimeOffset>("lastPresenceAt");
        if (lastPresenceAt == default) lastPresenceAt = ctx.Now;
        if (present) lastPresenceAt = ctx.Now;
        ctx.SetState("lastPresenceAt", lastPresenceAt);

        ctx.Emit("presence_any", present);

        var homeMode = ctx.Option("homeMode") is { Length: > 0 } h ? h : WellKnownModes.Home;
        var awayMode = ctx.Option("awayMode") is { Length: > 0 } a ? a : WellKnownModes.Away;

        // Manual-mode guard: with current_mode bound, act only while the home is in one of the two modes
        // this block manages — a manual Night/Vacation (or any custom mode) always wins over presence.
        var currentMode = ctx.Read("current_mode")?.ToString();
        var managed = currentMode is null
            || string.Equals(currentMode, homeMode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentMode, awayMode, StringComparison.OrdinalIgnoreCase);
        if (!managed) return;

        if (present)
        {
            ctx.Emit(CapabilityIds.HomeMode, homeMode);
            return;
        }

        var silentMin = (ctx.Now - lastPresenceAt).TotalMinutes;
        if (silentMin >= Math.Max(1, ctx.Param("awayDelayMin", 10)))
            ctx.Emit(CapabilityIds.HomeMode, awayMode);
        // Within the delay window: emit no mode — the previous state simply stands.
    }
}
