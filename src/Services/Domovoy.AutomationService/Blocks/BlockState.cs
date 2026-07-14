// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text.Json;

using Domovoy.AutomationService.Services;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Serialize/deserialize a block's state bag for persistence across restarts (roadmap Epic 2Q, Phase 2).
/// Values are the plain CLR primitives blocks keep (bool/double/DateTimeOffset/string) plus lists and nested
/// dictionaries (composite signal bags). On the way out, System.Text.Json renders <see cref="DateTimeOffset"/>
/// as an ISO-8601 string; on the way back, JSON is reduced to CLR primitives (numbers → double) so
/// <see cref="StateCoerce"/> can hand a block the exact type it asked for.
/// </summary>
public static class BlockStateJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyDictionary<string, object?> state) =>
        JsonSerializer.Serialize(state, Options);

    /// <summary>Parse persisted JSON back into a CLR state bag (JsonElement → bool/double/string/List/Dictionary).</summary>
    public static Dictionary<string, object?> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();

        var result = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            result[prop.Name] = FromElement(prop.Value);
        return result;
    }

    private static object? FromElement(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Null => null,
        JsonValueKind.Array => e.EnumerateArray().Select(FromElement).ToList(),
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => FromElement(p.Value)),
        _ => null,
    };
}

/// <summary>
/// Coerces a stored state value to the type a block asks for via <c>GetState&lt;T&gt;</c> (roadmap Epic 2Q,
/// Phase 2). Within a live session state is already the right CLR type; only after a persistence round-trip does
/// a value arrive in a wider form (a <see cref="DateTimeOffset"/> as an ISO string, an <c>int</c> as a double,
/// a typed list as <c>List&lt;object&gt;</c>). This bridges that gap so restart is transparent to a block.
/// </summary>
public static class StateCoerce
{
    public static T? As<T>(object? v)
    {
        if (v is null) return default;
        if (v is T typed) return typed;

        var target = typeof(T);
        try
        {
            if (target == typeof(double)) return (T)(object)Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (target == typeof(long)) return (T)(object)Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (target == typeof(int)) return (T)(object)Convert.ToInt32(v, CultureInfo.InvariantCulture);
            if (target == typeof(bool)) return (T)(object)ValueOps.AsBool(v);
            if (target == typeof(string)) return (T)(object)(v.ToString() ?? string.Empty);
            if (target == typeof(DateTimeOffset))
                return v is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                    ? (T)(object)dto : default;
            if (target == typeof(List<double>) && v is System.Collections.IEnumerable en)
                return (T)(object)en.Cast<object?>()
                    .Select(x => Convert.ToDouble(x, CultureInfo.InvariantCulture)).ToList();
        }
        catch
        {
            // A malformed/incompatible restored value must never crash a tick — fall through to default.
        }
        return default;
    }
}
