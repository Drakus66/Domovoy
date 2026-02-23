namespace Domovoy.LightService;

using Services;
using MessageBus;
using Common.Configuration;
using Domovoy.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Domovoy.Common.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("LightService");

        try
        {
            var hostBuilder = Host.CreateDefaultBuilder(args);
            hostBuilder.ConfigureSerilog();
            hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddHttpClient();
                services.Configure<ServiceEndpoints>(context.Configuration.GetSection("ServiceEndpoints"));
                services.Configure<BaseServiceOptions>(context.Configuration.GetSection("BaseService"));
                services.Configure<RabbitMqConfig>(context.Configuration.GetSection("RabbitMQ"));
                services.AddSingleton<IMessageBus, RabbitMqConnection>();
                services.AddSingleton<LightManager>();
                services.AddHostedService<LightManager>();
            });

            var host = hostBuilder.Build();
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
