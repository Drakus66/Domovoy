namespace Domovoy.AutomationService;

using Configuration;
using Services;

using Domovoy.Common.Logging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("AutomationService");

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.ConfigureSerilog();

            builder.Services.Configure<AutomationOptions>(
                builder.Configuration.GetSection(AutomationOptions.SectionName));
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

            // Typed HttpClient to the DbGateway (rules + device read-model).
            builder.Services.AddHttpClient<DbGatewayClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AutomationOptions>>().Value;
                client.BaseAddress = new Uri(options.DbGatewayBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            builder.Services.AddSingleton<DeviceRegistry>();
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AutomationOptions>>().Value;
                return new SunCalculator(options.Latitude, options.Longitude);
            });
            builder.Services.AddSingleton<RuleStore>();
            builder.Services.AddSingleton<RuleEvaluator>();
            builder.Services.AddSingleton<ActionExecutor>();
            builder.Services.AddSingleton<RuleRunner>();
            builder.Services.AddSingleton<HomeModeState>();

            // Order matters only loosely: RefreshLoop seeds rules/devices/mode, the engine + scheduler fire them.
            builder.Services.AddHostedService<RefreshLoop>();
            builder.Services.AddHostedService<AutomationEngine>();
            builder.Services.AddHostedService<AutomationScheduler>();
            builder.Services.AddHostedService<HomeModeMonitor>();   // 1G: track current home mode from the bus
            builder.Services.AddHostedService<PresenceMonitor>();   // 1G: presence-driven Home/Away switching

            var host = builder.Build();

            // Generic host has no Kestrel — expose Prometheus metrics on a standalone server (:9090).
            var metricServer = new MetricServer(port: 9090);
            metricServer.Start();

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
