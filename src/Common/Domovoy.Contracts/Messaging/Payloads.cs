// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Messaging;

using Domovoy.Contracts.Devices;

/// <summary>
/// Payloads carried inside <see cref="Envelope{T}.Data"/>. These are the wire contracts that the
/// migration moves the system onto; they are capability-addressed, not device-type-specific.
/// </summary>

/// <summary>A device was discovered (or re-announced) by an adapter. Carries the full descriptor.</summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceDiscovered"/>.</remarks>
public sealed record DeviceDiscoveredV1(DeviceDescriptor Device);

/// <summary>
/// A normalized device state update — capability id → value (already decoded by the adapter codec,
/// e.g. <c>brightness</c> 0..100, <c>on_off</c> bool). Replaces raw protocol payloads on the bus.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceState"/>.</remarks>
public sealed record DeviceStateReportV1(
    Guid DeviceId,
    IReadOnlyDictionary<string, object?> State);

/// <summary>
/// A capability-addressed command — set one or more capabilities (e.g. <c>on_off</c>=true,
/// <c>brightness</c>=50). The owning adapter encodes this into the protocol-native payload.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceCommand"/>.</remarks>
public sealed record DeviceCommandV1(
    Guid DeviceId,
    IReadOnlyDictionary<string, object?> Set);

/// <summary>A device's reachability changed.</summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceOnlineChanged"/>.</remarks>
public sealed record DeviceOnlineChangedV1(
    Guid DeviceId,
    bool IsOnline);

/// <summary>
/// An automation rule fired (roadmap Epic 1A). Emitted by the AutomationService after evaluating a
/// rule so the DbGateway can persist run history (AutoHistory) and the UI can show "why" (Epic 1F).
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.AutomationTriggered"/>.</remarks>
public sealed record AutomationTriggeredV1(
    string RuleId,
    string RuleName,
    DateTimeOffset FiredAt,
    bool ConditionsMet,
    bool Success,
    string TriggerSummary,
    int ActionsExecuted,
    string? Detail = null);

/// <summary>
/// A control block ran and its decision changed (roadmap Epic 1H). Published by the BlockRuntime on a
/// <b>meaningful</b> tick — when the block's emitted outputs change or its tick errors — so the block-layer,
/// which is an active entity like a rule, gets a first-class run record in the Activity Center. Unlike a
/// rule (Epic 1A) a block does not "fire and skip"; it ticks continuously, so the runtime deduplicates on
/// the emitted signature and publishes only on change, including in Shadow stage where a governor emits its
/// proposal but drives nothing. The DbGateway persists it to <c>block_history</c> for the <c>block</c>
/// Activity source; it is intentionally separate from device deltas and rule runs.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.BlockTriggered"/>.</remarks>
public sealed record BlockTriggeredV1(
    string BlockId,
    string BlockName,
    string TypeId,
    DateTimeOffset TickedAt,
    bool Ok,
    string Summary,
    string? Detail = null);

/// <summary>
/// The home mode changed (roadmap Epic 1G). Published by the DbGateway (the persistence authority for
/// the mode) after a manual or presence-driven switch. The AutomationService consumes it to feed
/// <c>Mode</c> conditions, and the DbGateway's own EventInterceptor consumes it to stamp the current
/// mode onto every event-log record as an ML feature (P0-5).
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.HomeModeChanged"/>.</remarks>
public sealed record HomeModeChangedV1(
    string Mode,
    string? PreviousMode,
    string Source,
    DateTimeOffset ChangedAt);
