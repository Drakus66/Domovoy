namespace Domovoy.Connectivity;

using Services;
using MessageBus;
using Adapters;
using Serilog;

using Domovoy.Common.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("ConnectivityService");

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.ConfigureSerilog();

            // Configure Message Bus (RabbitMQ)
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMq"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

            // Zigbee bridge state cache (shared between adapter and HTTP endpoint)
            builder.Services.AddSingleton<ZigbeeBridgeCache>();

            // Register Adapters
            builder.Services.AddSingleton<IProtocolAdapter, DomovoyNativeAdapter>();
            builder.Services.AddSingleton<IProtocolAdapter, Zigbee2MqttAdapter>();

            // Add Adapter Manager (Connectivity Service)
            builder.Services.AddHostedService<AdapterManager>();

            var host = builder.Build();
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
