// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.MessageBus;

/// <summary>
/// Configuration options for RabbitMQ connection including MQTT support
/// </summary>
public class RabbitMqConfig
{
    /// <summary>
    /// The hostname of the RabbitMQ server
    /// </summary>
    public string HostName { get; set; } = "rabbitmq";
    
    /// <summary>
    /// Username for authentication
    /// </summary>
    public string UserName { get; set; } = "user";
    
    /// <summary>
    /// Password for authentication
    /// </summary>
    public string Password { get; set; } = "user";
    
    /// <summary>
    /// The virtual host to use
    /// </summary>
    public string VirtualHost { get; set; } = "/";
    
    /// <summary>
    /// AMQP port (default: 5672)
    /// </summary>
    public int Port { get; set; } = 5672;
    
    /// <summary>
    /// MQTT port (default: 1883)
    /// </summary>
    public int MqttPort { get; set; } = 1883;
    
    /// <summary>
    /// Whether to use MQTT instead of AMQP
    /// </summary>
    public bool UseMqtt { get; set; } = true;
    
    /// <summary>
    /// Default MQTT QoS level (0, 1, or 2)
    /// </summary>
    public int MqttDefaultQoS { get; set; } = 1;
    
    /// <summary>
    /// Default MQTT retain flag for messages
    /// </summary>
    public bool MqttDefaultRetain { get; set; } = true;
    
    /// <summary>
    /// Maximum reconnect attempts before fallback to AMQP
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;
    
    /// <summary>
    /// Reconnect interval in milliseconds
    /// </summary>
    public int ReconnectInterval { get; set; } = 5000;
    
    /// <summary>
    /// Whether to auto-create exchanges and queues
    /// </summary>
    public bool AutoCreateStructures { get; set; } = true;
}
