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
    /// Safety-floor rule (anti-freeze, CO2→ventilation, smoke→unlock). Protected rules are always
    /// evaluated, cannot be disabled or deleted from the API/UI, and are sourced from local config so
    /// they run even when the UI/DB is unavailable. See AutomationService safety floor.
    /// </summary>
    public bool IsProtected { get; set; }

    /// <summary>Any trigger firing starts evaluation (OR across triggers).</summary>
    public List<RuleTrigger> Triggers { get; set; } = new();

    /// <summary>All conditions must hold for actions to run (AND across conditions). Empty = always.</summary>
    public List<RuleCondition> Conditions { get; set; } = new();

    /// <summary>Actions executed in order when triggered and conditions hold.</summary>
    public List<RuleAction> Actions { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleStatus { Proposed, Approved, Active, Disabled }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerType { DeviceState, Time, Sun }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SunEvent { Sunrise, Sunset }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConditionType { DeviceState, TimeOfDay, Sun, Mode }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType { Command, Delay, Notify }

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
}
