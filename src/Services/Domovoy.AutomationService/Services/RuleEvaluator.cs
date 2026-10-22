// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;

using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Pure evaluation of triggers and conditions against the <see cref="DeviceRegistry"/>, clock and sun
/// state. Triggers fire on a state change (device-state) or a clock tick (time/sun); conditions are
/// AND-combined guards. Zone-scoped device checks match <i>any</i> device in the zone.
/// </summary>
public sealed class RuleEvaluator
{
    private readonly DeviceRegistry _registry;
    private readonly SunCalculator _sun;

    public RuleEvaluator(DeviceRegistry registry, SunCalculator sun)
    {
        _registry = registry;
        _sun = sun;
    }

    /// <summary>Does a device-state trigger match this specific capability change?</summary>
    public bool DeviceTriggerMatches(RuleTrigger t, Guid deviceId, string capabilityId, object? newValue, object? oldValue)
    {
        if (t.Type != TriggerType.DeviceState) return false;
        if (!string.IsNullOrEmpty(t.CapabilityId) &&
            !string.Equals(t.CapabilityId, capabilityId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!MatchesDeviceOrZone(t.DeviceId, t.ZoneId, deviceId)) return false;

        if (string.Equals(t.Operator, "changed", StringComparison.OrdinalIgnoreCase))
            return !ValueOps.ValuesEqual(oldValue, newValue);

        return ValueOps.Compare(newValue, t.Operator, t.Value);
    }

    /// <summary>Are all conditions satisfied right now?</summary>
    public bool ConditionsHold(IEnumerable<RuleCondition> conditions, DateTimeOffset now, string? mode)
    {
        foreach (var c in conditions)
            if (!ConditionHolds(c, now, mode))
                return false;
        return true;
    }

    /// <summary>Does a single condition hold right now? Public so <see cref="RequiredExpressionEvaluator"/>
    /// (Epic 3E) can reuse the exact same predicate for the live "required expression" gate.</summary>
    public bool ConditionHolds(RuleCondition c, DateTimeOffset now, string? mode) => c.Type switch
    {
        ConditionType.DeviceState => DeviceConditionHolds(c),
        ConditionType.TimeOfDay => TimeWindowHolds(c, now),
        ConditionType.Sun => _sun.IsDark(now.ToUniversalTime()) == (c.Dark ?? true),
        ConditionType.Mode => !string.IsNullOrEmpty(mode) && string.Equals(mode, c.Mode, StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    private bool DeviceConditionHolds(RuleCondition c)
    {
        if (string.IsNullOrEmpty(c.CapabilityId)) return false;

        if (!string.IsNullOrEmpty(c.DeviceId) && Guid.TryParse(c.DeviceId, out var id))
            return ValueOps.Compare(_registry.GetValue(id, c.CapabilityId), c.Operator, c.Value);

        if (!string.IsNullOrEmpty(c.ZoneId))
            return _registry.DevicesInZone(c.ZoneId)
                .Any(d => ValueOps.Compare(_registry.GetValue(d, c.CapabilityId), c.Operator, c.Value));

        return false;
    }

    private static bool TimeWindowHolds(RuleCondition c, DateTimeOffset now)
    {
        if (!TryParseTime(c.FromTime, out var from) || !TryParseTime(c.ToTime, out var to)) return true;
        var t = now.TimeOfDay;
        return from <= to ? t >= from && t <= to : t >= from || t <= to; // wraps midnight
    }

    private bool MatchesDeviceOrZone(string? deviceId, string? zoneId, Guid actual)
    {
        if (!string.IsNullOrEmpty(deviceId))
            return string.Equals(deviceId, actual.ToString(), StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(zoneId))
            return string.Equals(_registry.GetZone(actual), zoneId, StringComparison.OrdinalIgnoreCase);
        return true; // neither specified ⇒ any device
    }

    private static bool TryParseTime(string? hhmm, out TimeSpan time) =>
        TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out time);
}
