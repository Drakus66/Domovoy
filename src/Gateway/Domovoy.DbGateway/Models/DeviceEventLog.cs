// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Append-only domain event-log record — the replayable feature store (roadmap P0-5). One record per
/// capability delta (or command). Persisted to the <c>device_events</c> MongoDB <b>time-series</b>
/// collection (timeField <see cref="Timestamp"/>, metaField <see cref="Meta"/>).
///
/// The schema is deliberately captured in full <i>now</i>: it carries not just "what became" but the
/// state delta (<see cref="OldValue"/>→<see cref="NewValue"/>), what triggered it
/// (<see cref="TriggerSource"/>), which rule/decision produced it, and the home mode/context — the
/// fuel for ML, replay and explainability (Epics 1F/2). A sparse "what became" log could not be
/// replayed or explained after the fact, so we fix the shape early. This is domain <i>data</i>,
/// kept separate from Serilog operational diagnostics.
/// </summary>
public class DeviceEventLog
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>When the change happened (UTC) — time-series timeField.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Low-cardinality grouping keys — time-series metaField.</summary>
    public EventMeta Meta { get; set; } = new();

    /// <summary>Capability whose value changed (on_off, brightness, temperature, …); empty for whole-device commands.</summary>
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>Previous value (state delta), null if unknown / first observation.</summary>
    public object? OldValue { get; set; }

    /// <summary>New value the capability changed to (or the commanded value).</summary>
    public object? NewValue { get; set; }

    /// <summary>What caused the change: <c>user</c> | <c>rule</c> | <c>device</c> | <c>ml</c>.</summary>
    public string TriggerSource { get; set; } = TriggerSources.Device;

    /// <summary>Rule that produced the action (links to AutoHistory), if any.</summary>
    public string? RuleId { get; set; }

    /// <summary>ML/automation decision id that produced the action, if any.</summary>
    public string? DecisionId { get; set; }

    /// <summary>Home mode at the time of the event (1G); null until modes exist.</summary>
    public string? Mode { get; set; }

    /// <summary>Extra context captured for ML features.</summary>
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>Correlates a state change back to the command that caused it (CloudEvents correlationid).</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>Time-series metaField for <see cref="DeviceEventLog"/> — the keys we filter/group by.</summary>
public class EventMeta
{
    public string DeviceId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    /// <summary><c>state_change</c> | <c>command</c>.</summary>
    public string Kind { get; set; } = EventKinds.StateChange;
}

/// <summary>Well-known trigger sources (open set — plugins/ML may add their own).</summary>
public static class TriggerSources
{
    public const string User = "user";
    public const string Rule = "rule";
    public const string Device = "device";
    public const string Ml = "ml";
}

/// <summary>Well-known event-log kinds.</summary>
public static class EventKinds
{
    public const string StateChange = "state_change";
    public const string Command = "command";
    /// <summary>A home-mode/context change (roadmap Epic 1G).</summary>
    public const string ModeChange = "mode_change";
}

/// <summary>Synthetic capability id used to record a home-mode change in the event-log (Epic 1G).</summary>
public static class ContextCapabilities
{
    public const string HomeMode = "home_mode";
}
