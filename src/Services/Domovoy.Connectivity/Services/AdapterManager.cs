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
///
/// <para><b>Resilience:</b> the broker may be unavailable at startup or drop mid-run. The manager connects
/// with retry+backoff (a failed connect must never bubble out of <see cref="ExecuteAsync"/> — an unhandled
/// exception in a <see cref="BackgroundService"/> stops the whole host, which under a restart policy becomes
/// an exit-0 crash loop), and auto-reconnects on disconnect, re-establishing each adapter's MQTT
/// subscriptions (a clean-session client loses them when the link drops).</para>
/// </summary>
public class AdapterManager : BackgroundService
{
    private readonly IEnumerable<IProtocolAdapter> _adapters;
    private readonly ILogger<AdapterManager> _logger;
    private readonly IOptions<RabbitMqConfig> _config;

    private IMqttClient? _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private string _brokerHost = "localhost";
    private int _brokerPort = 1883;
    private CancellationToken _stoppingToken;

    // Serializes (re)connect attempts so a disconnect firing mid-reconnect doesn't start a second loop.
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

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
        _stoppingToken = stoppingToken;
        _logger.LogInformation("AdapterManager starting with {Count} adapters...", _adapters.Count());

        _mqttOptions = BuildMqttOptions();
        _mqttClient = new MqttFactory().CreateMqttClient();
        _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessage;
        _mqttClient.DisconnectedAsync += OnMqttDisconnected;

        // Retry until connected or shutdown — never throw out of here (that would stop the host).
        if (!await ConnectWithRetryAsync(stoppingToken))
            return; // cancelled during startup

        foreach (var adapter in _adapters)
        {
            try
            {
                await adapter.StartAsync(_mqttClient, stoppingToken);
                _logger.LogInformation("Started adapter: {Adapter}", adapter.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start adapter: {Adapter}", adapter.Name);
            }
        }

        _logger.LogInformation("AdapterManager ready.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
                await Task.Delay(5000, stoppingToken);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private MqttClientOptions BuildMqttOptions()
    {
        _brokerHost = Environment.GetEnvironmentVariable("MQTT__BROKER") ?? _config.Value.HostName ?? "localhost";
        _brokerPort = int.TryParse(Environment.GetEnvironmentVariable("MQTT__PORT"), out var p) ? p : _config.Value.MqttPort;

        return new MqttClientOptionsBuilder()
            .WithTcpServer(_brokerHost, _brokerPort)
            .WithClientId("Domovoy.Connectivity")
            .WithCredentials(_config.Value.UserName, _config.Value.Password)
            .WithCleanSession()
            .Build();
    }

    /// <summary>
    /// Connects with capped exponential backoff, returning true once connected or false if cancelled. Never
    /// throws. The <see cref="_connectGate"/> ensures only one (re)connect loop runs at a time, so a disconnect
    /// event raised while a reconnect is already in flight is ignored rather than starting a competing loop.
    /// </summary>
    private async Task<bool> ConnectWithRetryAsync(CancellationToken token)
    {
        if (!await _connectGate.WaitAsync(0, CancellationToken.None))
            return false; // a (re)connect is already in progress

        try
        {
            var backoff = InitialBackoff;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _mqttClient!.ConnectAsync(_mqttOptions, token);
                    _logger.LogInformation(
                        "Connected to MQTT broker at {Host}:{Port} as user {User}", _brokerHost, _brokerPort, _config.Value.UserName);
                    return true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex, "MQTT connect to {Host}:{Port} failed; retrying in {Delay}s", _brokerHost, _brokerPort, (int)backoff.TotalSeconds);
                    try { await Task.Delay(backoff, token); }
                    catch (OperationCanceledException) { return false; }
                    backoff = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, backoff.TotalSeconds * 2));
                }
            }
            return false;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>Reconnects and restores every adapter's topic subscriptions after the MQTT link drops.</summary>
    private async Task OnMqttDisconnected(MqttClientDisconnectedEventArgs args)
    {
        if (_stoppingToken.IsCancellationRequested) return; // shutting down — don't fight the teardown

        _logger.LogWarning("MQTT disconnected ({Reason}); reconnecting…", args.Reason);

        if (!await ConnectWithRetryAsync(_stoppingToken))
            return;

        foreach (var adapter in _adapters)
        {
            try
            {
                await adapter.SubscribeAsync(_mqttClient!, _stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resubscribe adapter after reconnect: {Adapter}", adapter.Name);
            }
        }

        _logger.LogInformation("MQTT reconnected — {Count} adapter(s) resubscribed.", _adapters.Count());
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
