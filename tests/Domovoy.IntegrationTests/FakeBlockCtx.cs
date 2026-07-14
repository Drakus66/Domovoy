// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;

namespace Domovoy.IntegrationTests;

/// <summary>
/// In-memory <see cref="IBlockContext"/> for unit-testing a block in isolation (roadmap Epic 2Q). Extends the
/// original test fake with the typed <see cref="Options"/> channel and bool→number input coercion (mirroring the
/// real runtime), so option-driven and boolean-input primitives can be exercised without infrastructure.
/// </summary>
internal sealed class FakeBlockCtx : IBlockContext
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public string? ZoneId { get; set; }
    public string? ZoneKind { get; set; }
    public Dictionary<string, double> Params { get; } = new();
    public Dictionary<string, string> Options { get; } = new();
    public Dictionary<string, object?> Inputs { get; } = new();
    public Dictionary<string, object?> Commands { get; } = new();
    public Dictionary<string, object?> State { get; } = new();
    public Dictionary<string, object?> Emitted { get; } = new();

    public object? Get(string cap) => Emitted.TryGetValue(cap, out var v) ? v : null;
    public bool GetBool(string cap) => Get(cap) is bool b && b;
    public double? GetNumber(string cap) => Get(cap) is double d ? d : null;

    public object? Read(string inputPort) => Inputs.TryGetValue(inputPort, out var v) ? v : null;

    public double? ReadNumber(string inputPort) => Read(inputPort) switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        int i => i,
        long l => l,
        _ => null,
    };

    public double Param(string key, double fallback) => Params.TryGetValue(key, out var v) ? v : fallback;
    public string? Option(string key) => Options.TryGetValue(key, out var v) ? v : null;
    public object? Commanded(string capabilityId) => Commands.TryGetValue(capabilityId, out var v) ? v : null;
    public void Emit(string capabilityId, object? value) => Emitted[capabilityId] = value;
    // Mirror the real runtime: coerce restored values so persistence round-trips are faithful in tests too.
    public T? GetState<T>(string key) => State.TryGetValue(key, out var v) ? StateCoerce.As<T>(v) : default;
    public void SetState<T>(string key, T value) => State[key] = value!;
    public void Log(string message) { }
}
