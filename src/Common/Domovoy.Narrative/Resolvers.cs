// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>
/// Maps an event's coarse trigger source to a narrative persona (roadmap Epic 2N). Language-neutral: it
/// yields a <see cref="PersonaRole"/> token, not localized text. Trigger sources are the open P0-5 vocabulary
/// (<c>user</c>/<c>rule</c>/<c>device</c>/<c>ml</c>); a System virtual sensor (2L, <c>AdapterSource="System"</c>)
/// is impersonal regardless of trigger source. No per-person identity exists in Phase 2, so <c>user</c> is the
/// collective «домочадцы».
/// </summary>
public static class PersonaResolver
{
    public const string SystemAdapterSource = "System";

    public static PersonaRole Resolve(string? triggerSource, string? adapterSource)
    {
        if (string.Equals(adapterSource, SystemAdapterSource, StringComparison.OrdinalIgnoreCase))
            return PersonaRole.Impersonal;

        return (triggerSource ?? string.Empty).ToLowerInvariant() switch
        {
            "user" => PersonaRole.Residents,
            "ml" => PersonaRole.SpiritJudging,
            "rule" => PersonaRole.Spirit,
            // A device-originated change with no command is usually a physical interaction by a household
            // member (a wall switch, a door). Sensor-only devices are typically System (handled above).
            "device" => PersonaRole.Residents,
            _ => PersonaRole.Spirit,
        };
    }
}

/// <summary>
/// Derives the language-neutral <see cref="Transition"/> from a capability's old→new values (roadmap Epic 2N).
/// Uses the capability's semantics where it matters (contact/lock/occupancy) and falls back to on/off for
/// booleans and increase/decrease for numbers. Pure and deterministic.
/// </summary>
public static class TransitionResolver
{
    public static Transition Derive(string capabilityId, object? oldValue, object? newValue)
    {
        var newBool = AsBool(newValue);
        if (newBool is not null)
        {
            return capabilityId switch
            {
                CapabilityIds.Contact => newBool.Value ? Transition.Open : Transition.Close,
                CapabilityIds.Lock => newBool.Value ? Transition.Lock : Transition.Unlock,
                CapabilityIds.Occupancy => newBool.Value ? Transition.Enter : Transition.Off,
                _ => newBool.Value ? Transition.On : Transition.Off,
            };
        }

        var newNum = AsDouble(newValue);
        var oldNum = AsDouble(oldValue);
        if (newNum is not null && oldNum is not null)
        {
            if (newNum > oldNum) return Transition.Increase;
            if (newNum < oldNum) return Transition.Decrease;
        }

        return Transition.Set;
    }

    private static bool? AsBool(object? v) => v switch
    {
        null => null,
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        string s when s is "1" or "on" or "ON" => true,
        string s when s is "0" or "off" or "OFF" => false,
        _ => null,
    };

    private static double? AsDouble(object? v) => v switch
    {
        null => null,
        double d => d,
        int i => i,
        long l => l,
        float f => f,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };
}
