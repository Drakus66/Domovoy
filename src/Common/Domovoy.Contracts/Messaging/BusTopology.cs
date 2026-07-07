// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Messaging;

/// <summary>
/// Versioned <see cref="Envelope.Type"/> values. The <c>.vN</c> suffix is the contract version:
/// breaking changes introduce a new suffix so old and new consumers can coexist during migration.
/// </summary>
public static class MessageTypes
{
    public const string DeviceDiscovered = "domovoy.device.discovered.v1";
    public const string DeviceState = "domovoy.device.state.v1";
    public const string DeviceCommand = "domovoy.device.command.v1";
    public const string DeviceOnlineChanged = "domovoy.device.online.v1";
    public const string AutomationTriggered = "domovoy.automation.triggered.v1";
    public const string HomeModeChanged = "domovoy.home.mode.v1";

    // Plugin settings channel (Epic 2M tail): the plugin announces its settings schema, the supervisor
    // replies/broadcasts the effective values which the plugin applies live.
    public const string PluginSettingsSchema = "domovoy.plugin.settings.schema.v1";
    public const string PluginSettingsApplied = "domovoy.plugin.settings.applied.v1";
}

/// <summary>
/// Canonical bus topology — ONE naming convention (dotted <c>domovoy.&lt;domain&gt;</c> exchanges,
/// dotted routing keys). This is the migration target that replaces the mixed
/// <c>domovoy/discovery</c> (slash) / <c>device.commands</c> / <c>domovoy.state</c> constants
/// scattered across the codebase. All exchanges are AMQP topic exchanges.
/// </summary>
public static class BusTopology
{
    // Exchanges
    public const string DiscoveryExchange = "domovoy.discovery";
    public const string CommandsExchange = "domovoy.commands";
    public const string EventsExchange = "domovoy.events";
    public const string StateExchange = "domovoy.state";

    // Routing keys
    public const string DeviceDiscoveredKey = "device.discovered";
    public const string DeviceCommandKey = "device.command";
    public const string DeviceStateUpdatedKey = "device.state.updated";
    public const string DeviceOnlineChangedKey = "device.online.changed";
    public const string AutomationTriggeredKey = "automation.triggered";
    public const string HomeModeChangedKey = "home.mode.changed";

    // Plugin settings: schema is announced on one shared key (the supervisor binds it); effective values are
    // routed per-plugin so a plugin only receives its own settings (key = "plugin.settings.applied.{id}").
    public const string PluginSettingsSchemaKey = "plugin.settings.schema";
    public const string PluginSettingsAppliedKeyPrefix = "plugin.settings.applied";

    /// <summary>Per-plugin routing key for the effective-values message, e.g. <c>plugin.settings.applied.commute-planner</c>.</summary>
    public static string PluginSettingsAppliedKey(string pluginId) => $"{PluginSettingsAppliedKeyPrefix}.{pluginId}";
}
