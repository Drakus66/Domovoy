using System.Globalization;
using System.Text.Json;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Value normalization + comparison shared by the registry and rule evaluator. Values arrive as
/// <see cref="JsonElement"/> (bus payloads and rule definitions both deserialize <c>object</c> that
/// way), so everything is normalized to plain bool/double/string first to make comparisons uniform.
/// </summary>
public static class ValueOps
{
    /// <summary>Reduce a JsonElement/boxed value to bool, double or string (numbers → double).</summary>
    public static object? Normalize(object? v) => v switch
    {
        null => null,
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Null => null,
            _ => e.ToString()
        },
        bool b => b,
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToDouble(v, CultureInfo.InvariantCulture),
        _ => v
    };

    /// <summary>
    /// Evaluate <c>actual &lt;op&gt; expected</c>. Supported ops: eq, ne, gt, lt, gte, lte (default eq).
    /// Numbers compare numerically, otherwise string-insensitive equality (eq/ne only).
    /// </summary>
    public static bool Compare(object? actual, string? op, object? expected)
    {
        var a = Normalize(actual);
        var b = Normalize(expected);
        op = string.IsNullOrEmpty(op) ? "eq" : op.ToLowerInvariant();

        if (TryDouble(a, out var da) && TryDouble(b, out var db))
        {
            return op switch
            {
                "eq" => da == db,
                "ne" => da != db,
                "gt" => da > db,
                "lt" => da < db,
                "gte" => da >= db,
                "lte" => da <= db,
                _ => false
            };
        }

        var equal = string.Equals(ToText(a), ToText(b), StringComparison.OrdinalIgnoreCase);
        return op switch { "eq" => equal, "ne" => !equal, _ => false };
    }

    public static bool ValuesEqual(object? a, object? b) => Compare(a, "eq", b);

    /// <summary>
    /// Interpret a value as a presence/occupancy boolean: real bool, non-zero number, or a truthy string
    /// (<c>true/on/yes/1/detected/present/occupied</c>). Used by the presence-driven mode switch (1G).
    /// </summary>
    public static bool AsBool(object? v) => Normalize(v) switch
    {
        bool b => b,
        double d => d != 0,
        string s => s.Trim().ToLowerInvariant() is "true" or "on" or "yes" or "1" or "detected" or "present" or "occupied",
        _ => false
    };

    private static bool TryDouble(object? v, out double result)
    {
        switch (Normalize(v))
        {
            case double d: result = d; return true;
            case bool boolean: result = boolean ? 1 : 0; return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p):
                result = p; return true;
            default: result = 0; return false;
        }
    }

    private static string ToText(object? v) => v switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty
    };
}
