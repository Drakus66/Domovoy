// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Control-block SDK (roadmap Epic 1H). A block is a stateful component the runtime ticks periodically;
/// it reads bound inputs from the blackboard (the device registry), keeps its own state, and emits
/// capability values that are published as a virtual device's state. Built-in (E1) types implement
/// <see cref="IBlockType"/> + <see cref="IBlock"/>; instances are pure config (<see cref="Domovoy.Contracts.Blocks.ControlBlock"/>).
/// </summary>
public interface IBlock
{
    /// <summary>Advance the block one step: read inputs, update state, <see cref="IBlockContext.Emit"/> outputs.</summary>
    void Tick(IBlockContext ctx);
}

/// <summary>
/// Catalog categories for the authoring UI (roadmap Epic 2Q). With ~30 registered types a flat list is
/// unusable; the picker groups by these keys (localized client-side as <c>blocks:category.&lt;key&gt;</c>).
/// </summary>
public static class BlockCategories
{
    /// <summary>Ready-made loops — domain blocks and composites a non-expert reaches for first.</summary>
    public const string Template = "template";
    /// <summary>Closed-loop regulation: pid, hysteresis, ramp.</summary>
    public const string Control = "control";
    /// <summary>Signal conditioning: smoothing, spike rejection, debounce, slew.</summary>
    public const string Filter = "filter";
    /// <summary>Comparison and boolean combination: comparator, window, logic, select.</summary>
    public const string Logic = "logic";
    /// <summary>Timers, pulses, edges, latches, counters.</summary>
    public const string Time = "time";
    /// <summary>Value transforms: scaling, clamping, aggregation, custom expressions.</summary>
    public const string Math = "math";
    /// <summary>ML-backed blocks: predictors and governors.</summary>
    public const string Ml = "ml";
    /// <summary>Fallback for uncategorized (e.g. plugin-supplied) types.</summary>
    public const string Other = "other";
}

/// <summary>A registered block type: its schema (ports/params/outputs) and a factory for instances.</summary>
public interface IBlockType
{
    /// <summary>Stable type id used by <see cref="Domovoy.Contracts.Blocks.ControlBlock.TypeId"/>.</summary>
    string TypeId { get; }

    string Title { get; }
    string Description { get; }

    /// <summary>Catalog category key for the authoring UI picker (roadmap Epic 2Q). See <see cref="BlockCategories"/>.</summary>
    string Category => BlockCategories.Other;

    /// <summary>Bindable input ports (wired to a device+capability).</summary>
    IReadOnlyList<BlockPortSpec> Inputs { get; }

    /// <summary>Output capabilities — these become the virtual device's capabilities.</summary>
    IReadOnlyList<Capability> Outputs { get; }

    /// <summary>Tunable numeric parameters.</summary>
    IReadOnlyList<BlockParamSpec> Params { get; }

    /// <summary>
    /// Non-numeric (enum/bool/text) options — the escape from <see cref="BlockParamSpec"/> being numeric-only
    /// (roadmap Epic 2Q). A comparator's operator, a logic gate's function, an expression's formula live here.
    /// Default: none, so existing types need no change.
    /// </summary>
    IReadOnlyList<BlockOptionSpec> Options => Array.Empty<BlockOptionSpec>();

    IBlock Create();
}

/// <summary>
/// An input port: a named slot bound to some device capability of a given kind. <paramref name="Optional"/>
/// ports may be left unbound (e.g. a PID's feedforward input, an irrigation inhibit) — the block handles null.
/// </summary>
public sealed record BlockPortSpec(string Name, CapabilityKind Kind, string Description, bool Optional = false);

/// <summary>A tunable numeric parameter with a default and range (drives the typed authoring form).</summary>
public sealed record BlockParamSpec(string Name, double Default, string? Unit, double? Min, double? Max, string Description);

/// <summary>Kind of a non-numeric block option — drives the authoring control the UI renders.</summary>
public enum BlockOptionKind { Enum, Bool, Text }

/// <summary>
/// A non-numeric block option (roadmap Epic 2Q). For <see cref="BlockOptionKind.Enum"/>, <paramref name="Values"/>
/// lists the allowed choices. The value is always carried as a string in <see cref="Domovoy.Contracts.Blocks.ControlBlock.Options"/>.
/// </summary>
public sealed record BlockOptionSpec(
    string Name, BlockOptionKind Kind, string Default, string Description, IReadOnlyList<string>? Values = null);

/// <summary>
/// What a block sees while ticking: the clock, its bound inputs (read from the blackboard), its tunable
/// params, the latest commanded value of any writable output (setpoints), a private state bag, and a log.
/// </summary>
public interface IBlockContext
{
    DateTimeOffset Now { get; }

    /// <summary>The instance's assigned zone id, or null if unassigned (Epic 2I: drives model-scope resolution).</summary>
    string? ZoneId { get; }

    /// <summary>The kind/type of the instance's zone (e.g. "room", "greenhouse"), or null if unknown (Epic 2I).</summary>
    string? ZoneKind { get; }

    /// <summary>Current (normalized) value of a bound input port, or null if unbound/unknown.</summary>
    object? Read(string inputPort);

    /// <summary>Convenience: the bound input as a double, or null if missing/non-numeric.</summary>
    double? ReadNumber(string inputPort);

    /// <summary>Parameter value (falls back to <paramref name="fallback"/> when not configured).</summary>
    double Param(string key, double fallback);

    /// <summary>
    /// Non-numeric option value (enum/bool/text), or null when not configured (roadmap Epic 2Q). Default
    /// implementation returns null so existing <see cref="IBlockContext"/> implementations need no change.
    /// </summary>
    string? Option(string key) => null;

    /// <summary>Latest value commanded to a writable output capability (e.g. a setpoint), or null.</summary>
    object? Commanded(string capabilityId);

    /// <summary>Publish an output capability value (becomes part of the virtual device's state).</summary>
    void Emit(string capabilityId, object? value);

    /// <summary>Read a private per-instance state slot.</summary>
    T? GetState<T>(string key);

    /// <summary>Write a private per-instance state slot.</summary>
    void SetState<T>(string key, T value);

    void Log(string message);
}
