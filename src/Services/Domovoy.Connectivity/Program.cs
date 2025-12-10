using Domovoy.Connectivity.Services;
using Domovoy.MessageBus;
using Domovoy.Connectivity.Adapters;

var builder = Host.CreateApplicationBuilder(args);

// Configure Message Bus (RabbitMQ)
builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

// Register Adapters
builder.Services.AddSingleton<IProtocolAdapter, DomovoyNativeAdapter>();
builder.Services.AddSingleton<IProtocolAdapter, Zigbee2MqttAdapter>();

// Add Adapter Manager (Connectivity Service)
builder.Services.AddHostedService<AdapterManager>();

var host = builder.Build();
host.Run();
