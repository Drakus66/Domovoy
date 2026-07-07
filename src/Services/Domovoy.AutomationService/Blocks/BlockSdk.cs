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

/// <summary>A registered block type: its schema (ports/params/outputs) and a factory for instances.</summary>
public interface IBlockType
{
    /// <summary>Stable type id used by <see cref="Domovoy.Contracts.Blocks.ControlBlock.TypeId"/>.</summary>
    string TypeId { get; }

    string Title { get; }
    string Description { get; }

    /// <summary>Bindable input ports (wired to a device+capability).</summary>
    IReadOnlyList<BlockPortSpec> Inputs { get; }

    /// <summary>Output capabilities — these become the virtual device's capabilities.</summary>
    IReadOnlyList<Capability> Outputs { get; }

    /// <summary>Tunable numeric parameters.</summary>
    IReadOnlyList<BlockParamSpec> Params { get; }

    IBlock Create();
}

/// <summary>An input port: a named slot bound to some device capability of a given kind.</summary>
public sealed record BlockPortSpec(string Name, CapabilityKind Kind, string Description);

/// <summary>A tunable numeric parameter with a default and range (drives the typed authoring form).</summary>
public sealed record BlockParamSpec(string Name, double Default, string? Unit, double? Min, double? Max, string Description);

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
