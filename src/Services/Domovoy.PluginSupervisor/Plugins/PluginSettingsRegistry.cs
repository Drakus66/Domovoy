// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;
using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Plugins;
using Domovoy.MessageBus;
using Domovoy.PluginSupervisor.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.PluginSupervisor.Plugins;

/// <summary>
/// Owns the plugin-settings channel on the supervisor side (roadmap Epic 2M tail). A running plugin announces
/// its settings schema over the bus; this registry caches it (so the plugins panel can show a settings button),
/// persists the operator's chosen values under <c>{PluginsRoot}/.settings/{id}.json</c> (outside the plugin
/// folder, so they survive reinstall), and — as the reply to a schema announce and on every UI save — publishes
/// the effective values back to the plugin, which applies them <b>live</b> (no restart).
/// </summary>
public sealed class PluginSettingsRegistry : BackgroundService
{
    private const string SchemaQueue = "plugin-supervisor-settings-schema-v1";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SupervisorOptions _options;
    private readonly IMessageBus _bus;
    private readonly ILogger<PluginSettingsRegistry> _logger;
    private readonly object _fileLock = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<PluginSettingDescriptor>> _schemas = new();

    public PluginSettingsRegistry(
        IOptions<SupervisorOptions> options,
        IMessageBus bus,
        ILogger<PluginSettingsRegistry> logger)
    {
        _options = options.Value;
        _bus = bus;
        _logger = logger;
    }

    private string SettingsDir => Path.Combine(_options.PluginsRoot, ".settings");
    private string PathFor(string id) => Path.Combine(SettingsDir, id + ".json");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Load persisted schemas so the settings button is available even before a plugin re-announces
        // (e.g. right after a supervisor restart, or while the plugin is stopped).
        LoadCachesFromDisk();

        await _bus.SubscribeAsync<Envelope<PluginSettingsSchemaV1>>(
            SchemaQueue,
            BusTopology.EventsExchange,
            BusTopology.PluginSettingsSchemaKey,
            OnSchemaAsync,
            stoppingToken);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>True when the plugin has a known, non-empty settings schema (drives the panel's settings button).</summary>
    public bool HasSchema(string pluginId) =>
        _schemas.TryGetValue(pluginId, out var s) && s.Count > 0;

    /// <summary>Schema + current values for the UI, with secret values masked (never returned in the clear).</summary>
    public PluginSettingsView? GetSettings(string pluginId)
    {
        if (!_schemas.TryGetValue(pluginId, out var schema) || schema.Count == 0) return null;

        var stored = Load(pluginId) ?? new StoredSettings(schema.ToList(), new());
        var values = new Dictionary<string, object?>(EffectiveValues(stored), StringComparer.Ordinal);
        foreach (var d in schema)
            if (d.Secret) values[d.Key] = ""; // mask; the UI shows a "leave blank to keep" field
        return new PluginSettingsView(schema, values);
    }

    /// <summary>Merge + persist the operator's values and broadcast them to the plugin. False if no schema is known.</summary>
    public bool UpdateSettings(string pluginId, IReadOnlyDictionary<string, object?> incoming)
    {
        if (!_schemas.TryGetValue(pluginId, out var schema) || schema.Count == 0) return false;

        var stored = Load(pluginId) ?? new StoredSettings(schema.ToList(), new());
        var byKey = schema.ToDictionary(d => d.Key, d => d, StringComparer.Ordinal);
        foreach (var (key, value) in incoming)
        {
            if (!byKey.TryGetValue(key, out var d)) continue;    // ignore keys not in the schema
            if (d.Secret && IsBlank(value)) continue;            // keep the stored secret when left blank
            stored.Values[key] = value;
        }

        Save(pluginId, stored);
        _ = PublishAppliedAsync(pluginId, EffectiveValues(stored), CancellationToken.None);
        _logger.LogInformation("Updated settings for plugin {PluginId}", pluginId);
        return true;
    }

    private async Task OnSchemaAsync(Envelope<PluginSettingsSchemaV1> envelope)
    {
        var data = envelope.Data;
        if (data is null || string.IsNullOrWhiteSpace(data.PluginId)) return;

        var stored = Load(data.PluginId);
        var merged = new StoredSettings(data.Settings.ToList(), stored?.Values ?? new());
        Save(data.PluginId, merged);
        _schemas[data.PluginId] = merged.Settings;

        _logger.LogInformation("Cached settings schema for plugin {PluginId} ({Count} setting(s))",
            data.PluginId, merged.Settings.Count);

        // Hand the freshly-started plugin its saved configuration (defaults overlaid with operator values).
        await PublishAppliedAsync(data.PluginId, EffectiveValues(merged), CancellationToken.None);
    }

    private async Task PublishAppliedAsync(string pluginId, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        try
        {
            var envelope = Envelope<PluginSettingsAppliedV1>.Create(
                MessageTypes.PluginSettingsApplied,
                source: "plugin-supervisor",
                data: new PluginSettingsAppliedV1(pluginId, values),
                subject: pluginId);
            await _bus.PublishAsync(
                BusTopology.EventsExchange,
                BusTopology.PluginSettingsAppliedKey(pluginId),
                envelope, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish applied settings for plugin {PluginId}", pluginId);
        }
    }

    /// <summary>Declared defaults overlaid with any persisted operator values → the full set the plugin should run with.</summary>
    private static Dictionary<string, object?> EffectiveValues(StoredSettings stored)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var d in stored.Settings)
            result[d.Key] = stored.Values.TryGetValue(d.Key, out var v) ? v : d.Default;
        return result;
    }

    private void LoadCachesFromDisk()
    {
        if (!Directory.Exists(SettingsDir)) return;
        foreach (var file in Directory.EnumerateFiles(SettingsDir, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var stored = Load(id);
            if (stored is { Settings.Count: > 0 })
                _schemas[id] = stored.Settings;
        }
    }

    private StoredSettings? Load(string pluginId)
    {
        var path = PathFor(pluginId);
        lock (_fileLock)
        {
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path), Json); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read settings for plugin {PluginId}", pluginId);
                return null;
            }
        }
    }

    private void Save(string pluginId, StoredSettings stored)
    {
        lock (_fileLock)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(PathFor(pluginId), JsonSerializer.Serialize(stored, Json));
        }
    }

    private static bool IsBlank(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrEmpty(s),
        JsonElement { ValueKind: JsonValueKind.Null } => true,
        JsonElement { ValueKind: JsonValueKind.String } je => string.IsNullOrEmpty(je.GetString()),
        _ => false,
    };

    /// <summary>Persisted form: the last-announced schema plus the operator's chosen values.</summary>
    private sealed record StoredSettings(
        List<PluginSettingDescriptor> Settings,
        Dictionary<string, object?> Values);
}

/// <summary>Schema + (masked) current values returned to the UI.</summary>
public sealed record PluginSettingsView(
    IReadOnlyList<PluginSettingDescriptor> Settings,
    IReadOnlyDictionary<string, object?> Values);
