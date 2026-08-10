// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

namespace Domovoy.Contracts.Devices;

using Domovoy.Contracts.Capabilities;

/// <summary>
/// Normalized device state keyed by capability id (e.g. <c>on_off</c> → true, <c>brightness</c> → 100).
/// This is the single representation produced by adapter codecs and consumed by handlers, the UI,
/// automations and ML — replacing the protocol-native, per-handler ad-hoc dictionaries.
/// <para>
/// Deliberately thin: build it, <see cref="Set"/> values into it, read <see cref="Values"/>. It used to
/// also carry typed accessors and per-capability shortcuts (<c>OnOff()</c>, <c>Temperature()</c>,
/// <c>TryGetDouble</c>…) that nothing ever called — the consumers that decode wire values do it against
/// <see cref="System.Text.Json.JsonElement"/> directly, where the raw payload actually lives.
/// </para>
/// </summary>
public sealed class CapabilityState
{
    private readonly Dictionary<string, object?> _values = new();

    /// <summary>Raw capability-id → value map.</summary>
    public IReadOnlyDictionary<string, object?> Values => _values;

    public CapabilityState Set(string capabilityId, object? value)
    {
        _values[capabilityId] = value;
        return this;
    }
}
