// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Common.Configuration;

/// <summary>
/// Bus topology constants for Zigbee-bridge management — the one domain that predates
/// <c>Domovoy.Contracts.Messaging.BusTopology</c> and still has its own exchange.
/// <para>
/// Всё остальное ходит по capability-контракту (DeviceDiscoveredV1 / DeviceStateReportV1 /
/// DeviceCommandV1) через <c>BusTopology</c>. Пара <c>device.events</c> / <c>device.state.updated</c>
/// жила здесь ради legacy-события <c>DeviceStateUpdatedEvent</c>, которое переизлучал
/// UnifiedDeviceService; сервис снят, релей SignalR читает контракт напрямую — константы ушли вместе
/// с ним.
/// </para>
/// </summary>
public class MessageBusConfiguration
{
    // Zigbee bridge management (permit-join, rename, remove, bridge state/info/network events).
    public const string ZigbeeBridgeExchange = "zigbee.bridge";
    public const string ZigbeeBridgeStateRoutingKey = "zigbee.bridge.state";
    public const string ZigbeeBridgeInfoRoutingKey = "zigbee.bridge.info";
    public const string ZigbeeNetworkEventRoutingKey = "zigbee.network.event";
    public const string ZigbeeBridgeCommandsQueue = "zigbee.bridge.commands.queue";
    public const string ZigbeeBridgeCommandRoutingKey = "zigbee.bridge.command";
}
