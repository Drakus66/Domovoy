// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Common.Configuration;
using Domovoy.Connectivity.Adapters;
using Domovoy.MessageBus;

using MQTTnet;
using MQTTnet.Client;

using Microsoft.Extensions.Options;

namespace Domovoy.Connectivity.Services;

/// <summary>
/// Owns the single MQTT client for the Connectivity service and routes inbound messages to the
/// adapter that claims their topic. Each adapter handles its own bus publish/subscribe (capability
/// contract, bridge commands) directly via <c>IMessageBus</c> — the manager does NOT route bus traffic.
/// </summary>
public class AdapterManager : BackgroundService
{
    private readonly IEnumerable<IProtocolAdapter> _adapters;
    private readonly ILogger<AdapterManager> _logger;
    private readonly IOptions<RabbitMqConfig> _config;
    private IMqttClient? _mqttClient;

    public AdapterManager(
        IEnumerable<IProtocolAdapter> adapters,
        IOptions<RabbitMqConfig> config,
        ILogger<AdapterManager> logger)
    {
        _adapters = adapters;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AdapterManager starting with {Count} adapters...", _adapters.Count());

        await ConnectToMqtt(stoppingToken);

        foreach (var adapter in _adapters)
        {
            try
            {
                await adapter.StartAsync(_mqttClient!, stoppingToken);
                _logger.LogInformation("Started adapter: {Adapter}", adapter.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start adapter: {Adapter}", adapter.Name);
            }
        }

        _logger.LogInformation("AdapterManager ready.");

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(5000, stoppingToken);
    }

    private async Task ConnectToMqtt(CancellationToken token)
    {
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        var brokerHost = Environment.GetEnvironmentVariable("MQTT__BROKER") ?? _config.Value.HostName ?? "localhost";
        var brokerPort = int.TryParse(Environment.GetEnvironmentVariable("MQTT__PORT"), out var p) ? p : _config.Value.MqttPort;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId("Domovoy.Connectivity")
            .WithCredentials(_config.Value.UserName, _config.Value.Password)
            .WithCleanSession()
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessage;

        await _mqttClient.ConnectAsync(options, token);
        _logger.LogInformation("Connected to MQTT Broker at {Host}:{Port} as user {User}", brokerHost, brokerPort, _config.Value.UserName);
    }

    private async Task HandleMqttMessage(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = System.Text.Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

        foreach (var adapter in _adapters)
        {
            if (adapter.CanHandleTopic(topic))
                await adapter.HandleMessageAsync(topic, payload);
        }
    }
}
