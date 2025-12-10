namespace Domovoy.DeviceService;

using Services;
using MessageBus;
using Common.Configuration;
using Domovoy.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

internal class Program
{
    static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(theme: AnsiConsoleTheme.Code)
            .CreateLogger();

        var hostBuilder = Host.CreateDefaultBuilder(args);
        hostBuilder.UseSerilog();
        hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHttpClient();
            services.Configure<ServiceEndpoints>(context.Configuration.GetSection("ServiceEndpoints"));
            services.Configure<BaseServiceOptions>(context.Configuration.GetSection("BaseService"));
            services.AddSingleton<IMessageBus, RabbitMqConnection>();
            services.AddSingleton<IMqttDeviceAdapter, MqttDeviceAdapter>();
            services.AddHostedService<DeviceManager>();
        });

        var host = hostBuilder.Build();
        host.Run();
    }
}
