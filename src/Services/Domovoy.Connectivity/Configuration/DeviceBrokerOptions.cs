// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Connectivity.Configuration;

/// <summary>
/// The MQTT broker that carries <b>device</b> traffic — Zigbee2MQTT topics and Domovoy Native.
///
/// <para>A separate contour from the message bus, and separate settings. In the reference deployment both
/// live in the same RabbitMQ container, which is exactly why they used to share one settings class: the
/// connectivity service read the broker port out of <c>RabbitMqConfig</c>. That made pointing devices at
/// their own broker (a standalone Mosquitto, a vendor gateway) impossible to express without also
/// reconfiguring the bus.</para>
///
/// <para>Bound from the <c>Mqtt</c> section, i.e. the <c>MQTT__BROKER</c> / <c>MQTT__PORT</c> /
/// <c>MQTT__USERNAME</c> / <c>MQTT__PASSWORD</c> environment variables compose already sets.</para>
/// </summary>
public sealed class DeviceBrokerOptions
{
    public const string Section = "Mqtt";

    /// <summary>Broker host. Defaults to the same container the reference compose runs the bus in.</summary>
    public string Broker { get; set; } = "rabbitmq";

    public int Port { get; set; } = 1883;

    public string UserName { get; set; } = "user";

    public string Password { get; set; } = "user";
}
