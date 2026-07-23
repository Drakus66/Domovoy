// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services;

/// <summary>
/// In-process fan-out of live capability changes (roadmap Epic 3E), separate from the durable
/// <see cref="DeviceRegistry"/> blackboard: <see cref="AutomationEngine"/> publishes every
/// <c>(deviceId, capabilityId, newValue)</c> it sees on the bus, and short-lived waiters subscribe for the
/// duration of a single await — <see cref="ActionExecutor"/>'s <c>WaitForEvent</c> action (event-driven,
/// not polling) and <see cref="RuleRunner"/>'s required-expression live gate (re-evaluate + cancel pending
/// actions when it turns false). A plain C# event is enough: handlers are cheap (a comparison + a
/// <c>TaskCompletionSource</c> set) and always unsubscribe in a <c>finally</c>, so this never accumulates.
/// </summary>
public sealed class DeviceEventBroker
{
    public event Action<Guid, string, object?>? StateChanged;

    public void Publish(Guid deviceId, string capabilityId, object? value) =>
        StateChanged?.Invoke(deviceId, capabilityId, value);
}
