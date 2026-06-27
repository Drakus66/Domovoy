using System.Globalization;
using System.Text.Json;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Encodes a <c>state_change</c> event's new value into a numeric ML label (roadmap Epic 2I, Phase 1) so a
/// boolean target (on_off, occupancy, …) can train a classifier from the event-log the same way a numeric
/// target trains from telemetry. Booleans map to 0/1; truthy strings ("on"/"true"/"open"/…) and numbers are
/// coerced. Returns null for values that carry no usable label (so the caller drops the sample).
/// </summary>
public static class EventLabelEncoder
{
    private static readonly HashSet<string> Truthy = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "on", "yes", "1", "open", "opened", "detected", "present", "occupied", "locked", "active",
    };

    private static readonly HashSet<string> Falsy = new(StringComparer.OrdinalIgnoreCase)
    {
        "false", "off", "no", "0", "closed", "clear", "absent", "unoccupied", "unlocked", "inactive",
    };

    /// <summary>Map a JSON event value to a 0/1 (or numeric) label, or null if it carries none.</summary>
    public static double? ToLabel(JsonElement? value)
    {
        if (value is not { } v) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Number => v.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String => FromString(v.GetString()),
            _ => null,
        };
    }

    /// <summary>
    /// Map a JSON event value to a categorical class label for a multiclass (enum) target (Epic 2I, Phase 3):
    /// the string value as-is, or a number/bool rendered to a stable token. Null/empty → null (drop the sample).
    /// </summary>
    public static string? ToClass(JsonElement? value)
    {
        if (value is not { } v) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => v.TryGetDouble(out var d) ? d.ToString(CultureInfo.InvariantCulture) : null,
            _ => null,
        };
    }

    private static double? FromString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (Truthy.Contains(t)) return 1;
        if (Falsy.Contains(t)) return 0;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
