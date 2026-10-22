// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Plugins;

using Microsoft.Extensions.Logging;

namespace Domovoy.MessageBus;

/// <summary>
/// Plugin-side helper for the settings channel (roadmap Epic 2M tail). A plugin declares the settings it
/// exposes, then <see cref="StartAsync"/>: it <b>first subscribes</b> to its effective-values messages, then
/// <b>announces its schema</b> — so the supervisor's reply (the saved values) is never missed. Values arrive
/// live whenever the operator saves in the UI; <see cref="Current"/> always reflects the latest effective
/// configuration (defaults merged with what has been applied) and <see cref="Changed"/> fires on every update.
/// </summary>
public sealed class PluginSettingsService
{
    private readonly IMessageBus _bus;
    private readonly string _pluginId;
    private readonly IReadOnlyList<PluginSettingDescriptor> _schema;
    private readonly ILogger? _logger;

    private readonly object _lock = new();
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private volatile bool _received;

    /// <summary>Raised after new effective values are applied. The argument is the merged snapshot.</summary>
    public event Action<IReadOnlyDictionary<string, object?>>? Changed;

    public PluginSettingsService(
        IMessageBus bus,
        string pluginId,
        IReadOnlyList<PluginSettingDescriptor> schema,
        ILogger? logger = null)
    {
        _bus = bus;
        _pluginId = pluginId;
        _schema = schema;
        _logger = logger;

        // Seed with the declared defaults so Current is usable before the supervisor replies.
        foreach (var d in schema)
            _values[d.Key] = d.Default;
    }

    /// <summary>Effective values (declared defaults overlaid with whatever the operator has applied).</summary>
    public IReadOnlyDictionary<string, object?> Current
    {
        get { lock (_lock) return new Dictionary<string, object?>(_values, StringComparer.Ordinal); }
    }

    /// <summary>
    /// Subscribe for applied values first, then announce the schema. The announce is <b>retried</b> until the
    /// supervisor replies: on a fresh bus the plugin can publish before the supervisor's schema queue is bound
    /// (a topic exchange drops a message with no bound queue), so a one-shot announce can be lost. Re-announcing
    /// until the first applied message arrives makes discovery robust to that startup race.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var queue = $"plugin-{_pluginId}-settings-v1";
        await _bus.SubscribeAsync<Envelope<PluginSettingsAppliedV1>>(
            queue,
            BusTopology.EventsExchange,
            BusTopology.PluginSettingsAppliedKey(_pluginId),
            OnAppliedAsync,
            ct);

        // Первое объявление — до возврата из StartAsync. Раньше в фон уходил весь цикл, и сразу после
        // старта было неопределённо, ушла схема или ещё нет: наблюдаемого момента «объявлено» не
        // существовало вовсе. Повторы остаются фоновыми.
        await AnnounceAsync(ct);
        _logger?.LogInformation("Announced {Count} plugin setting(s) for {PluginId}", _schema.Count, _pluginId);

        _ = Task.Run(() => ReannounceUntilAckedAsync(ct), ct);
    }

    private async Task AnnounceAsync(CancellationToken ct)
    {
        try
        {
            var envelope = Envelope<PluginSettingsSchemaV1>.Create(
                MessageTypes.PluginSettingsSchema,
                source: $"plugin:{_pluginId}",
                data: new PluginSettingsSchemaV1(_pluginId, _schema),
                subject: _pluginId);
            await _bus.PublishAsync(BusTopology.EventsExchange, BusTopology.PluginSettingsSchemaKey, envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to announce settings for {PluginId}; will retry", _pluginId);
        }
    }

    private async Task ReannounceUntilAckedAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_received)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
            catch (OperationCanceledException) { break; }

            if (_received) break;
            await AnnounceAsync(ct);
        }
    }

    private Task OnAppliedAsync(Envelope<PluginSettingsAppliedV1> envelope)
    {
        var data = envelope.Data;
        if (data is null || !string.Equals(data.PluginId, _pluginId, StringComparison.Ordinal))
            return Task.CompletedTask;

        _received = true; // acks the announce loop (values may repeat when we re-announced before the reply landed)

        IReadOnlyDictionary<string, object?> snapshot;
        bool changed = false;
        lock (_lock)
        {
            foreach (var (key, value) in data.Values)
            {
                // Compare rendered JSON so a repeated identical apply doesn't churn (e.g. rebuild the backend).
                if (!_values.TryGetValue(key, out var existing) || !SameJson(existing, value))
                {
                    _values[key] = value; // values arrive as JsonElement over the wire; Get<T> coerces on read
                    changed = true;
                }
            }
            snapshot = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        }

        if (!changed) return Task.CompletedTask;

        _logger?.LogInformation("Applied plugin settings for {PluginId}: {Keys}",
            _pluginId, string.Join(", ", data.Values.Keys));
        Changed?.Invoke(snapshot);
        return Task.CompletedTask;
    }

    private static bool SameJson(object? a, object? b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    /// <summary>Reads a setting, coercing the stored value (CLR default or bus-delivered JsonElement) to <typeparamref name="T"/>.</summary>
    public T Get<T>(string key, T fallback)
    {
        object? raw;
        lock (_lock)
        {
            if (!_values.TryGetValue(key, out raw) || raw is null) return fallback;
        }
        return Coerce(raw, fallback);
    }

    /// <summary>Coerce a raw value to <typeparamref name="T"/>, tolerating <see cref="JsonElement"/> from the wire.</summary>
    internal static T Coerce<T>(object? raw, T fallback)
    {
        if (raw is null) return fallback;
        if (raw is T typed) return typed;

        try
        {
            if (raw is JsonElement je)
            {
                var converted = FromJson(je, fallback);
                return converted is T t ? t : fallback;
            }

            // Plain CLR value of a different-but-convertible type (e.g. int default vs. long, or string number).
            if (typeof(T) == typeof(string)) return (T)(object)(raw.ToString() ?? "");
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Wire value → <typeparamref name="T"/>. The discriminator is <c>typeof(T)</c>, deliberately: it used
    /// to be <c>default(T)</c>, and for <c>T = string</c> that is <c>null</c>, so the string branch was
    /// unreachable and a non-string JSON value stored for a string setting fell through to
    /// <c>Deserialize&lt;string&gt;()</c>, threw, and silently became the fallback instead of its raw text.
    /// </summary>
    private static object? FromJson<T>(JsonElement je, T fallback)
    {
        if (typeof(T) == typeof(string))
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText();

        if (typeof(T) == typeof(bool))
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(je.GetString(), out var b) && b,
                _ => false,
            };

        // InvariantCulture обязательна: значение пришло из JSON, а там разделитель дробной части —
        // всегда точка. С культурой хоста "60.5" на русской локали не разбиралось и молча становилось
        // значением по умолчанию.
        if (typeof(T) == typeof(double))
            return je.ValueKind == JsonValueKind.Number ? je.GetDouble()
                : double.TryParse(je.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

        if (typeof(T) == typeof(int))
            return je.ValueKind == JsonValueKind.Number ? je.GetInt32()
                : int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

        return je.Deserialize<T>();
    }
}
