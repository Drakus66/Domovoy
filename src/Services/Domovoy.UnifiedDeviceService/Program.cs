namespace Domovoy.UnifiedDeviceService;

using Services;
using MessageBus;
using Common.Configuration;
using Domovoy.Common.Services;
using Domovoy.Common.Services.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Domovoy.Common.Logging;

using Domovoy.UnifiedDeviceService.Services.Identity;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("UnifiedDeviceService");

        try
        {
            var hostBuilder = Host.CreateDefaultBuilder(args);
            hostBuilder.ConfigureSerilog();
            hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddHttpClient();
                services.Configure<ServiceEndpoints>(context.Configuration.GetSection("ServiceEndpoints"));
                services.Configure<BaseServiceOptions>(context.Configuration.GetSection("BaseService"));

                // Register device type handlers
                services.AddSingleton<IDeviceTypeHandler, GenericDeviceHandler>();
                services.AddSingleton<IDeviceTypeHandler, LightDeviceHandler>();
                services.AddSingleton<IDeviceTypeHandler, SensorDeviceHandler>();
                
                // Register identity resolver
                services.AddSingleton<IDeviceIdentityResolver, DeviceIdentityResolver>();

                // Register the unified manager
                services.AddHostedService<UnifiedDeviceManager>();
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
