namespace Domovoy.MessageBus;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Domovoy.Common.Configuration;

using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Linq;

public class RabbitMqConnection : IMessageBus, IAsyncDisposable
{
    // Poison messages are dead-lettered here instead of being requeued forever; each queue gets a
    // matching "<queue>.dlq" bound by its own name so failures stay inspectable per consumer.
    private const string DeadLetterExchange = "domovoy.dlx";
    private const ushort PrefetchCount = 50;
    private const int MaxConnectAttempts = 6;

    private readonly RabbitMqConfig _config;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly Dictionary<string, IAsyncBasicConsumer> _consumers;
    private readonly ConcurrentDictionary<string, bool> _declaredExchanges = new();
    private readonly ConcurrentDictionary<string, bool> _queueHasDlq = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly bool _useMqtt;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;
    private readonly int _mqttDefaultQoS;
    private readonly bool _mqttDefaultRetain;

    /// <summary>
    /// Returns true if MQTT mode is enabled
    /// </summary>
    public bool IsMqttEnabled => _useMqtt;

    public RabbitMqConnection(
        IOptions<RabbitMqConfig> config,
        ILogger<RabbitMqConnection> logger)
    {
        // The connection is established lazily on first publish/subscribe (with retry + backoff)
        // so DI construction never blocks and a broker outage at startup doesn't hang the host.
        _config = config.Value;
        _logger = logger;
        _consumers = new Dictionary<string, IAsyncBasicConsumer>();
        _useMqtt = config.Value.UseMqtt;
        _mqttDefaultQoS = config.Value.MqttDefaultQoS;
        _mqttDefaultRetain = config.Value.MqttDefaultRetain;
    }

    /// <summary>
    /// Returns an open channel, (re)connecting with exponential backoff when needed. Automatic
    /// recovery handles mid-run broker restarts; this handles the broker being down at call time.
    /// </summary>
    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var channel = _channel;
        if (channel is { IsOpen: true }) return channel;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;

            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (_connection is not { IsOpen: true })
                    {
                        var factory = new ConnectionFactory
                        {
                            HostName = _config.HostName,
                            UserName = _config.UserName,
                            Password = _config.Password,
                            VirtualHost = _config.VirtualHost,
                            // Always use AMQP port for the actual connection, even if MQTT mode is enabled
                            Port = _config.Port,
                            AutomaticRecoveryEnabled = true,
                            TopologyRecoveryEnabled = true,
                        };

                        _connection = await factory.CreateConnectionAsync(cancellationToken);
                    }

                    _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                    // Bounded prefetch so a slow consumer (e.g. Mongo writes) doesn't buffer the whole queue.
                    await _channel.BasicQosAsync(0, PrefetchCount, global: false, cancellationToken);
                    // Topology is per-channel state from our point of view — re-declare after reconnect.
                    _declaredExchanges.Clear();

                    _logger.LogInformation(
                        "Successfully connected to RabbitMQ ({Mode}) on port {Port}",
                        _useMqtt ? "MQTT-compatible" : "AMQP",
                        _config.Port);
                    return _channel;
                }
                catch (Exception ex) when (attempt < MaxConnectAttempts && !cancellationToken.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                    _logger.LogWarning(ex,
                        "RabbitMQ connect attempt {Attempt}/{Max} failed; retrying in {Delay}s",
                        attempt, MaxConnectAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken cancellationToken)
    {
        if (_declaredExchanges.TryAdd(exchange, true))
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
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
            var channel = await EnsureChannelAsync(cancellationToken);

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

            await DeclareExchangeAsync(channel, exchange, cancellationToken);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, basicProperties: properties, body, cancellationToken: cancellationToken);

            _logger.LogDebug("Message published to {Exchange} with routing key {RoutingKey}, QoS: {QoS}, Retain: {Retain}",
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

            var channel = await EnsureChannelAsync(cancellationToken);
            await DeclareExchangeAsync(channel, exchange, cancellationToken);
            channel = await DeclareQueueWithDeadLetterAsync(channel, queue, arguments, cancellationToken);
            await channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            var consumerChannel = channel;
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = JsonSerializer.Deserialize<T>(body);
                    if (!Equals(message, default(T)))
                    {
                        await handler(message);
                        // Ack with CancellationToken.None: once the handler succeeded, the ack must go
                        // through even during shutdown, or the message is redelivered and reprocessed.
                        await consumerChannel.BasicAckAsync(ea.DeliveryTag, false, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                    // Dead-letter instead of requeue so a poison message can't loop forever; requeue
                    // only when the queue predates the DLQ topology and has none configured.
                    var requeue = !_queueHasDlq.GetValueOrDefault(queue);
                    await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, requeue, CancellationToken.None);
                }
            };

            await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken: cancellationToken);
            _consumers[queue] = consumer;

            _logger.LogInformation("Subscribed to {Queue} with routing key {RoutingKey}, QoS: {QoS}", queue, routingKey, qos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to {Queue}", queue);
            throw;
        }
    }

    /// <summary>
    /// Declares the consumer queue with dead-letter arguments plus its companion "<c>{queue}.dlq</c>".
    /// A queue created by an older version has different arguments — redeclaring then fails with
    /// PRECONDITION_FAILED and closes the channel, so we reopen it and fall back to the legacy
    /// declaration (keeping the old requeue behaviour for that queue).
    /// </summary>
    private async Task<IChannel> DeclareQueueWithDeadLetterAsync(
        IChannel channel, string queue, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var withDlq = new Dictionary<string, object?>(arguments)
        {
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = queue,
        };

        try
        {
            await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync($"{queue}.dlq", durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync($"{queue}.dlq", DeadLetterExchange, queue, cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, arguments: withDlq, cancellationToken: cancellationToken);
            _queueHasDlq[queue] = true;
            return channel;
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
        {
            _logger.LogWarning(
                "Queue {Queue} exists with different arguments — declaring without dead-lettering (failed messages will be requeued)",
                queue);
            _channel = null; // the failed declaration closed the channel
            var fresh = await EnsureChannelAsync(cancellationToken);
            await fresh.QueueDeclareAsync(queue, durable: true, exclusive: false, arguments: arguments, cancellationToken: cancellationToken);
            _queueHasDlq[queue] = false;
            return fresh;
        }
    }

    public async Task UnsubscribeAsync(string queue, CancellationToken cancellationToken = default)
    {
        if (_channel is null) return;

        if (_consumers.TryGetValue(queue, out var consumer))
        {
            var asyncConsumer = (AsyncEventingBasicConsumer)consumer;
            await _channel.BasicCancelAsync(asyncConsumer.ConsumerTags[0], cancellationToken: cancellationToken);
            _consumers.Remove(queue);
            _logger.LogInformation("Unsubscribed from {Queue}", queue);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { if (_channel is not null) await _channel.DisposeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing RabbitMQ channel"); }

        try { if (_connection is not null) await _connection.DisposeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing RabbitMQ connection"); }

        _connectLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Bounded sync fallback for containers that dispose synchronously; never hang shutdown
        // on a slow/offline broker.
        try { DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error during RabbitMQ dispose"); }
    }
}
