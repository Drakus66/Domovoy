using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog.Sinks.Grafana.Loki;
using Microsoft.Extensions.DependencyInjection;

namespace Domovoy.Common.Logging;

public static class SerilogBootstrap
{
    public static void Initialize(string serviceName = "Domovoy.Service")
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Ocelot", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(
                theme: AnsiConsoleTheme.Code,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.GrafanaLoki(
                Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://loki:3100")
            .CreateLogger();
    }

    public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog();
    }

    public static IServiceCollection ConfigureSerilog(this IServiceCollection services)
    {
        return services.AddSerilog();
    }
}
