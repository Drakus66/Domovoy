using Domovoy.DbGateway.Config;
using Domovoy.DbGateway.Serializers;
using Domovoy.DbGateway.Services;
using Domovoy.MessageBus;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Spins up <b>real</b> RabbitMQ + MongoDB (Testcontainers) and runs one shared <see cref="EventInterceptor"/>
/// against them — the running DbGateway's persistence path. Lets tests verify the end-to-end capability
/// flow (adapter → bus → Mongo) and Mongo time-series serialization for real (roadmap Phase 1.5). Because
/// the infra is local containers (loopback), these tests also exercise the offline-first invariant — no
/// external network is touched.
/// </summary>
public sealed class InfraFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().WithImage("rabbitmq:3.13-management").Build();
    private readonly MongoDbContainer _mongo = new MongoDbBuilder().WithImage("mongo:7").Build();
    private EventInterceptor? _interceptor;
    private static int _serializersRegistered;

    public IMongoDatabase Db { get; private set; } = null!;
    public IMessageBus Bus { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_rabbit.StartAsync(), _mongo.StartAsync());

        // Mirror DbGateway Program.cs serializer registration (global, once per process).
        if (Interlocked.Exchange(ref _serializersRegistered, 1) == 0)
        {
            BsonSerializer.RegisterSerializer(new ObjectSerializer());
            BsonSerializer.RegisterSerializer(new JsonElementSerializer());
            BsonSerializer.RegisterSerializer(new JsonObjectDictionarySerializer());
        }

        Db = new MongoClient(_mongo.GetConnectionString()).GetDatabase("DomovoyTest");

        var amqp = new Uri(_rabbit.GetConnectionString());
        var creds = amqp.UserInfo.Split(':');
        var cfg = new RabbitMqConfig
        {
            HostName = amqp.Host,
            Port = amqp.Port,
            UserName = Uri.UnescapeDataString(creds[0]),
            Password = Uri.UnescapeDataString(creds.Length > 1 ? creds[1] : string.Empty),
            VirtualHost = "/",
            UseMqtt = false,
        };
        Bus = new RabbitMqConnection(Options.Create(cfg), NullLogger<RabbitMqConnection>.Instance);

        // One shared interceptor consuming the bus into Mongo (as the live DbGateway does).
        _interceptor = new EventInterceptor(
            Bus, Db, Options.Create(new TelemetryOptions()), NullLogger<EventInterceptor>.Instance);
        await _interceptor.StartAsync(default);
        await Task.Delay(2500); // let subscriptions bind before tests publish (topic exchange drops unrouted)
    }

    public async Task DisposeAsync()
    {
        if (_interceptor is not null) await _interceptor.StopAsync(default);
        Bus?.Dispose();
        await _rabbit.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}

[CollectionDefinition("infra")]
public sealed class InfraCollection : ICollectionFixture<InfraFixture>;
