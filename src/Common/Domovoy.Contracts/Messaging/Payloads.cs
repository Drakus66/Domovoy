// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Messaging;

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Notifications;

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
/// An operator request to restart a service (or all of them) from the UI. Each .NET service listens; when the
/// target matches its own name (or <c>"all"</c>) it stops gracefully, and the container restart policy
/// (<c>restart: unless-stopped</c>) brings it back — no privileged Docker access needed. Container-level control
/// of arbitrary/infra containers is a separate, opt-in path (Docker socket).
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.SystemControl"/>.</remarks>
public sealed record SystemControlV1(
    string Target,   // a service name (matches the container/compose name) or "all"
    string Action);  // "restart" (the only action a service can perform on itself)

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
/// A user-facing notification was raised (2M.2 LAN channel + off-LAN push, Epic 2O.4). Published by the
/// AutomationService's SignalR notification channel; the ApiGateway relays it to connected clients over the
/// DeviceHub (the in-app banner while the client is in LAN / open), and the ntfy channel forwards it to a
/// self-hosted push server for delivery when the phone is asleep / off-LAN. <see cref="Severity"/> is
/// info/warning/critical (drives banner colour + push priority); <see cref="Category"/> is the optional
/// reactive/proactive/optimization taxonomy that Epic 3F (notification discipline) will route per-type.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.NotificationRaised"/>.</remarks>
public sealed record NotificationRaisedV1(
    string Title,
    string Body,
    string Severity,
    DateTimeOffset RaisedAt,
    string? Category = null,
    IReadOnlyList<NotificationAction>? Actions = null);

/// <summary>
/// A presence/location report from a geofencing source (roadmap Epic 3D) — chiefly the OwnTracks-compatible
/// ingest in the ApiGateway, which normalizes OwnTracks <c>location</c>/<c>transition</c> payloads onto this.
/// The AutomationService's presence layer resolves the resident from <see cref="ResidentKeys"/> (matched
/// against each resident's <c>OwnTracksId</c>), applies the server-side geofence (distance from the site
/// location vs the configured radius) or the <see cref="ExplicitPresent"/> transition, and republishes the
/// resident's virtual person device + the occupancy aggregate. Kept source-agnostic: a room-level or mmWave
/// source (later layers) can publish the same contract with <see cref="ExplicitPresent"/> set and no coords.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.PresenceReported"/>.</remarks>
public sealed record PresenceReportedV1(
    IReadOnlyList<string> ResidentKeys,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters,
    int? BatteryPercent,
    bool? ExplicitPresent,
    DateTimeOffset ReportedAt);

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
