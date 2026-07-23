// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Automations;

using System.Text.Json.Serialization;

/// <summary>
/// A deterministic automation rule (roadmap Epic 1A): <c>trigger → condition → action</c>. Shared by
/// the AutomationService (engine), DbGateway (persistence) and ApiGateway/WebUI. Modelled as plain
/// mutable classes so the same shape serializes cleanly to both JSON (wire/UI) and BSON (Mongo) — rules
/// are configuration, not high-throughput bus messages, so one shape avoids a dual model here.
/// </summary>
public class AutomationRule
{
    /// <summary>Stable rule id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Lifecycle status. Only <see cref="RuleStatus.Active"/> rules execute (Proposed/Approved/Disabled do not).</summary>
    public RuleStatus Status { get; set; } = RuleStatus.Active;

    /// <summary>
    /// Legacy "protected rule" flag. The hardcoded local safety-floor it once gated was removed — every
    /// rule is now an ordinary user automation stored in the DB (visible/editable/deletable from the UI),
    /// so this is always <c>false</c>. Kept only for BSON back-compat with rules already persisted with the
    /// field; the API forces it false and the engine no longer reads it.
    /// </summary>
    public bool IsProtected { get; set; }

    /// <summary>Any trigger firing starts evaluation (OR across triggers).</summary>
    public List<RuleTrigger> Triggers { get; set; } = new();

    /// <summary>All conditions must hold for actions to run (AND across conditions). Empty = always.</summary>
    public List<RuleCondition> Conditions { get; set; } = new();

    /// <summary>
    /// Live gate (roadmap Epic 3E, Hubitat "Required Expression"): must hold both to start the run AND
    /// continuously while it executes — unlike <see cref="Conditions"/> (checked once at fire time), a
    /// required expression turning false mid-run cancels any pending action (e.g. inside a
    /// <see cref="ActionType.Delay"/> or <see cref="ActionType.WaitForEvent"/> action). Null/empty = no live
    /// gate (only <see cref="Conditions"/> apply).
    /// </summary>
    public RequiredExpression? RequiredExpression { get; set; }

    /// <summary>Actions executed in order when triggered and conditions hold.</summary>
    public List<RuleAction> Actions { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Roadmap Epic 3E: a boolean combination of named guards (Hubitat "Required Expression"), evaluated live —
/// see <see cref="AutomationRule.RequiredExpression"/>. <see cref="Conditions"/> reuses the exact same
/// predicate shape as <see cref="RuleCondition"/> (device-state/time-of-day/sun/mode) so it needs no new
/// value language; <see cref="Expression"/> combines them by index (<c>C0</c>, <c>C1</c>, …) with
/// <c>&amp;&amp; || ! ( )</c>. A blank expression defaults to AND of every condition (the common case).
/// </summary>
public class RequiredExpression
{
    public List<RuleCondition> Conditions { get; set; } = new();

    /// <summary>e.g. <c>"C0 &amp;&amp; (C1 || !C2)"</c>. Blank ⇒ AND of all conditions.</summary>
    public string Expression { get; set; } = string.Empty;
}

/// <summary>
/// Rule lifecycle / staged rollout (roadmap Epics 1A + 1F): <c>Proposed → Shadow → BoundedActive → Active</c>.
/// <see cref="Active"/> rules execute freely. <see cref="Shadow"/> rules ARE evaluated and their would-be
/// actions are logged to run history, but no commands are published — the "log what it would have done" stage
/// (ML proposals from Phase 2 land here first). <see cref="BoundedActive"/> rules DO execute, but no more
/// often than a cooldown window — bounding actuation rate (blast radius) while trust is still building, the
/// stage between Shadow and full Active. <see cref="Proposed"/>/<see cref="Approved"/>/<see cref="Disabled"/>
/// are not evaluated. New members are appended so persisted (BSON) ordinals stay stable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleStatus { Proposed, Approved, Active, Disabled, Shadow, BoundedActive }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerType { DeviceState, Time, Sun }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SunEvent { Sunrise, Sunset }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConditionType { DeviceState, TimeOfDay, Sun, Mode }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType { Command, Delay, Notify, Scene, WaitForEvent }

/// <summary>
/// What starts a rule. <see cref="TriggerType.DeviceState"/>: a capability of a device (or any device
/// in a zone) crossing a comparison. <see cref="TriggerType.Time"/>: a cron schedule.
/// <see cref="TriggerType.Sun"/>: sunrise/sunset (with optional offset) for outdoor lighting.
/// </summary>
public class RuleTrigger
{
    public TriggerType Type { get; set; }

    // DeviceState
    public string? DeviceId { get; set; }
    public string? ZoneId { get; set; }
    public string? CapabilityId { get; set; }
    /// <summary>eq | ne | gt | lt | gte | lte | changed (default eq; "changed" fires on any change).</summary>
    public string? Operator { get; set; }
    public object? Value { get; set; }

    // Time
    /// <summary>5-field cron (min hour day-of-month month day-of-week), evaluated per minute.</summary>
    public string? Cron { get; set; }

    // Sun
    public SunEvent? Sun { get; set; }
    /// <summary>Minutes offset from the sun event (negative = before, positive = after).</summary>
    public int OffsetMinutes { get; set; }
}

/// <summary>A guard that must hold for actions to run. Combined with AND.</summary>
public class RuleCondition
{
    public ConditionType Type { get; set; }

    // DeviceState
    public string? DeviceId { get; set; }
    public string? ZoneId { get; set; }
    public string? CapabilityId { get; set; }
    /// <summary>eq | ne | gt | lt | gte | lte (default eq).</summary>
    public string? Operator { get; set; }
    public object? Value { get; set; }

    // TimeOfDay ("HH:mm"); window may wrap midnight (From > To).
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }

    // Sun: true ⇒ require it to currently be dark (after sunset / before sunrise).
    public bool? Dark { get; set; }

    // Mode (home mode, 1G); matched case-insensitively.
    public string? Mode { get; set; }
}

/// <summary>
/// What the rule does. <see cref="ActionType.Command"/>: set capabilities on a device.
/// <see cref="ActionType.Delay"/>: wait before the next action (enables "on for 5 min" = command,
/// delay, command). <see cref="ActionType.Notify"/>: emit a notification message.
/// <see cref="ActionType.Scene"/>: activate a stored scene (Epic 3B) — fans its targets out as commands.
/// <see cref="ActionType.WaitForEvent"/> (Epic 3E): pause the run until a device-state match or a timeout,
/// then either fall through to the next action (event arrived) or run <see cref="OnTimeout"/> and stop
/// (timed out) — mid-action branching, e.g. "wait for the door to open ≤5 min, else branch B".
/// </summary>
public class RuleAction
{
    public ActionType Type { get; set; }

    // Command
    public string? DeviceId { get; set; }
    public Dictionary<string, object?>? Set { get; set; }

    // Delay
    public int DelaySeconds { get; set; }

    // Notify
    public string? Message { get; set; }

    // Scene (Epic 3B): id of the scene to activate; resolved to its device targets at run time.
    public string? SceneId { get; set; }

    // WaitForEvent (Epic 3E) — same device-state match shape as RuleTrigger/RuleCondition.
    public string? WaitDeviceId { get; set; }
    public string? WaitZoneId { get; set; }
    public string? WaitCapabilityId { get; set; }
    /// <summary>eq | ne | gt | lt | gte | lte (default eq).</summary>
    public string? WaitOperator { get; set; }
    public object? WaitValue { get; set; }
    /// <summary>Seconds to wait before giving up (clamped to a sane max; &lt;=0 defaults to 300s).</summary>
    public int TimeoutSeconds { get; set; }
    /// <summary>Branch B: run only when the wait timed out (event never matched). Terminal — the main
    /// action sequence does not resume after it. Empty/null ⇒ a timeout just falls through silently.</summary>
    public List<RuleAction>? OnTimeout { get; set; }

    /// <summary>
    /// Error branch (Epic 3E, Homey-style "error card"): if THIS action throws, run these instead of
    /// aborting the whole rule run. Terminal — the main sequence does not resume after it. In a
    /// <see cref="RuleAction.Message"/> inside the branch, the token <c>{error}</c> is substituted with the
    /// caught exception's message (the "error text as context" requirement). Empty/null ⇒ current behavior:
    /// an unhandled action failure aborts the rest of the run (recorded as a failed run).
    /// </summary>
    public List<RuleAction>? OnError { get; set; }
}
