// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Common.Configuration;

/// <summary>
/// Bus topology constants used by the SignalR relay path and by Zigbee-bridge management.
/// All capability-contract publishes (DeviceDiscoveredV1 / DeviceStateReportV1 / DeviceCommandV1)
/// use <c>Domovoy.Contracts.Messaging.BusTopology</c> instead of constants here.
/// </summary>
public class MessageBusConfiguration
{
    // CapabilityDeviceManager re-emits normalized state as DeviceStateUpdatedEvent on this exchange;
    // ApiGateway.EventRelayService and DbGateway.EventInterceptor subscribe to it.
    public const string DeviceEventsExchange = "device.events";
    public const string DeviceStateUpdatedRoutingKey = "device.state.updated";

    // Zigbee bridge management (permit-join, rename, remove, bridge state/info/network events).
    public const string ZigbeeBridgeExchange = "zigbee.bridge";
    public const string ZigbeeBridgeStateRoutingKey = "zigbee.bridge.state";
    public const string ZigbeeBridgeInfoRoutingKey = "zigbee.bridge.info";
    public const string ZigbeeNetworkEventRoutingKey = "zigbee.network.event";
    public const string ZigbeeBridgeCommandsQueue = "zigbee.bridge.commands.queue";
    public const string ZigbeeBridgeCommandRoutingKey = "zigbee.bridge.command";
}
