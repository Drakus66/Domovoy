// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.MessageBus;

/// <summary>
/// Connection settings for the message bus — AMQP, and only AMQP.
///
/// <para>This is <b>not</b> the device MQTT broker. RabbitMQ happens to host both in the reference
/// deployment, but they are different contours: the bus carries the internal capability contract, the
/// MQTT broker carries Zigbee2MQTT / Domovoy Native device traffic. The connectivity service configures
/// that one separately (<c>DeviceBrokerOptions</c>, section <c>Mqtt</c>) — one settings class for two
/// brokers is how a change to one silently reconfigures the other.</para>
///
/// <para>Everything the bus retries or declares is a deliberate constant in
/// <see cref="RabbitMqConnection"/>, not a setting: options nobody reads look like knobs and are worse
/// than no options at all.</para>
/// </summary>
public class RabbitMqConfig
{
    /// <summary>The hostname of the RabbitMQ server.</summary>
    public string HostName { get; set; } = "rabbitmq";

    /// <summary>Username for authentication.</summary>
    public string UserName { get; set; } = "user";

    /// <summary>Password for authentication.</summary>
    public string Password { get; set; } = "user";

    /// <summary>The virtual host to use.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>AMQP port (default: 5672).</summary>
    public int Port { get; set; } = 5672;
}
