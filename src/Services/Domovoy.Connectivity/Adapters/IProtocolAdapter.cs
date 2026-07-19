// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

/// <summary>
/// A protocol adapter owns inbound MQTT topics for one ecosystem (Zigbee2MQTT, Domovoy Native, …)
/// and bridges them onto the capability contract. Outbound commands are received directly on the bus
/// by the adapter (subscribed as <c>Envelope&lt;DeviceCommandV1&gt;</c>) — no routing through the host.
/// </summary>
public interface IProtocolAdapter
{
    string Name { get; }

    Task StartAsync(IMqttClient mqttClient, CancellationToken token);
    Task StopAsync(CancellationToken token);

    /// <summary>
    /// (Re)establish this adapter's MQTT topic subscriptions on the given client. Called once by
    /// <see cref="StartAsync"/> and again after every MQTT reconnect — a clean-session client loses its
    /// subscriptions when the connection drops, so the manager re-runs this to restore them. One-time setup
    /// (bus subscriptions, background loops) belongs in <see cref="StartAsync"/>, not here. Default: nothing.
    /// </summary>
    Task SubscribeAsync(IMqttClient mqttClient, CancellationToken token) => Task.CompletedTask;

    /// <summary>True if this adapter owns the given MQTT topic.</summary>
    bool CanHandleTopic(string topic);

    /// <summary>Dispatch an inbound MQTT message (already routed by <see cref="CanHandleTopic"/>).</summary>
    Task HandleMessageAsync(string topic, string payload);
}
