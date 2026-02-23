namespace Domovoy.MessageBus;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Domovoy.Common.Configuration;

using RabbitMQ.Client.Events;
using System.Linq;

public class RabbitMqConnection : IMessageBus
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly Dictionary<string, IAsyncBasicConsumer> _consumers;
    private readonly bool _useMqtt;
    private bool _disposed;
    private readonly int _mqttDefaultQoS;
    private readonly bool _mqttDefaultRetain;

    public IChannel Channel => _channel;

    /// <summary>
    /// Returns true if MQTT mode is enabled
    /// </summary>
    public bool IsMqttEnabled => _useMqtt;

    public RabbitMqConnection(
        IOptions<RabbitMqConfig> config,
        ILogger<RabbitMqConnection> logger)
    {
        _logger = logger;
        _consumers = new Dictionary<string, IAsyncBasicConsumer>();
        _useMqtt = config.Value.UseMqtt;
        _mqttDefaultQoS = config.Value.MqttDefaultQoS;
        _mqttDefaultRetain = config.Value.MqttDefaultRetain;

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = config.Value.HostName,
                UserName = config.Value.UserName,
                Password = config.Value.Password,
                VirtualHost = config.Value.VirtualHost,
            };

            // Always use AMQP port for the actual connection, even if MQTT mode is enabled
            factory.Port = config.Value.Port;

            if (_useMqtt)
            {
                _logger.LogInformation("MQTT mode enabled. Configuration will be set up for MQTT compatibility (using AMQP port {Port})", factory.Port);
            }

            _connection = factory.CreateConnectionAsync().Result;
            _channel = _connection.CreateChannelAsync().Result;

            if (_useMqtt)
            {
                ConfigureMqttExchanges().Wait();
                _logger.LogInformation("Successfully connected to RabbitMQ (MQTT-compatible mode) on port {Port}", factory.Port);
            }
            else
            {
                _logger.LogInformation("Successfully connected to RabbitMQ using AMQP protocol on port {Port}", factory.Port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ. Will attempt to connect using fallback configuration.");

            // Try to connect with fallback settings if MQTT connection fails
            if (_useMqtt)
            {
                try
                {
                    _logger.LogInformation("Attempting fallback connection using AMQP...");

                    var factory = new ConnectionFactory
                    {
                        HostName = config.Value.HostName,
                        UserName = config.Value.UserName,
                        Password = config.Value.Password,
                        VirtualHost = config.Value.VirtualHost,
                        Port = config.Value.Port,
                    };

                    _connection = factory.CreateConnectionAsync().Result;
                    _channel = _connection.CreateChannelAsync().Result;

                    // Switch to AMQP mode since MQTT failed
                    _useMqtt = false;
                    _logger.LogInformation("Successfully connected to RabbitMQ using AMQP fallback on port {Port}", config.Value.Port);
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Failed to connect to RabbitMQ using fallback connection");
                    throw; // Re-throw as we've exhausted our options
                }
            }
            else
            {
                throw; // Re-throw as we weren't trying MQTT in the first place
            }
        }
    }

    private async Task ConfigureMqttExchanges()
    {
        // Configure the discovery exchange
        await _channel.ExchangeDeclareAsync(
            MessageBusConfiguration.DeviceDiscoveryExchange,
            "topic",
            durable: true,
            arguments: new Dictionary<string, object>
            {
                { "mqtt-subscription-qos", 1 },
                { "mqtt-subscription-retain", true }
            }!);

        // Configure the device commands exchange
        await _channel.ExchangeDeclareAsync(
            MessageBusConfiguration.DeviceCommandsExchange,
            "topic",
            durable: true,
            arguments: new Dictionary<string, object>
            {
                { "mqtt-subscription-qos", 1 },
                { "mqtt-subscription-retain", true }
            }!);

        // Configure the device events exchange
        await _channel.ExchangeDeclareAsync(
            MessageBusConfiguration.DeviceEventsExchange,
            "topic",
            durable: true,
            arguments: new Dictionary<string, object>
            {
                { "mqtt-subscription-qos", 1 },
                { "mqtt-subscription-retain", true }
            }!);

        // Configure the device data exchange
        await _channel.ExchangeDeclareAsync(
            MessageBusConfiguration.DeviceDataExchange,
            "topic",
            durable: true,
            arguments: new Dictionary<string, object>
            {
                { "mqtt-subscription-qos", 0 },
                { "mqtt-subscription-retain", false }
            }!);

        // Configure the device availability exchange
        await _channel.ExchangeDeclareAsync(
            MessageBusConfiguration.DeviceAvailabilityExchange,
            "topic",
            durable: true,
            arguments: new Dictionary<string, object>
            {
                { "mqtt-subscription-qos", 1 },
                { "mqtt-subscription-retain", true }
            }!);
    }

    public async Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
    {
        await PublishAsync(exchange, routingKey, message, _mqttDefaultQoS, _mqttDefaultRetain, cancellationToken);
    }

    /// <summary>
    /// Publishes a message with specified MQTT QoS and retain settings
    /// </summary>
    public async Task PublishAsync<T>(string exchange, string routingKey, T message, int qos, bool retain, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var properties = new BasicProperties();

            if (_useMqtt)
            {
                properties.Headers = new Dictionary<string, object>
                {
                    { "mqtt-qos", (byte)qos },
                    { "mqtt-retain", retain }
                }!;
            }

            await _channel.ExchangeDeclareAsync(exchange, "topic", durable: true, cancellationToken: cancellationToken);
            await _channel.BasicPublishAsync(exchange, routingKey, mandatory: true, basicProperties: properties, body, cancellationToken: cancellationToken);

            _logger.LogInformation("Message published to {Exchange} with routing key {RoutingKey}, QoS: {QoS}, Retain: {Retain}",
                exchange, routingKey, qos, retain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to {Exchange} with routing key {RoutingKey}",
                exchange, routingKey);
            throw;
        }
    }

    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, CancellationToken cancellationToken = default)
    {
        // Default QoS for subscriptions
        var qos = _useMqtt ? _mqttDefaultQoS : 0;
        await SubscribeAsync(queue, exchange, routingKey, handler, qos, cancellationToken);
    }

    /// <summary>
    /// Subscribes to a topic with specified MQTT QoS
    /// </summary>
    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey, Func<T, Task> handler, int qos, CancellationToken cancellationToken = default)
    {
        try
        {
            Dictionary<string, object?> arguments = [];

            if (_useMqtt)
            {
                arguments = new()
                {
                    { "mqtt-subscription-qos", (byte)qos }
                };

                // Use wildcards if routingKey ends with #
                // Convert MQTT wildcards to AMQP wildcards if needed
                if (routingKey.EndsWith('#'))
                {
                    routingKey = routingKey.Replace("#", "*");
                }
            }

            await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, arguments: arguments, cancellationToken: cancellationToken);
            await _channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = JsonSerializer.Deserialize<T>(body);
                    if (!Equals(message, default(T)))
                    {
                        await handler(message);
                        await _channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
                }
            };

            await _channel.BasicConsumeAsync(queue, false, consumer, cancellationToken: cancellationToken);
            _consumers[queue] = consumer;

            _logger.LogInformation("Subscribed to {Queue} with routing key {RoutingKey}, QoS: {QoS}", queue, routingKey, qos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to {Queue}", queue);
            throw;
        }
    }

    public async Task UnsubscribeAsync(string queue, CancellationToken cancellationToken = default)
    {
        if (_consumers.TryGetValue(queue, out var consumer))
        {
            var asyncConsumer = (AsyncEventingBasicConsumer)consumer;
            await _channel.BasicCancelAsync(asyncConsumer.ConsumerTags[0], cancellationToken: cancellationToken);
            _consumers.Remove(queue);
            _logger.LogInformation("Unsubscribed from {Queue}", queue);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _channel.DisposeAsync().AsTask().Wait();
        _connection.DisposeAsync().AsTask().Wait();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
