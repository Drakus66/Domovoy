namespace Domovoy.PluginSupervisor;

using Configuration;
using Plugins;

using Domovoy.Common.Logging;

using Prometheus;
using Serilog;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("PluginSupervisor");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

            builder.Services.Configure<SupervisorOptions>(
                builder.Configuration.GetSection(SupervisorOptions.SectionName));

            // The supervisor is both a hosted background service and an injectable for the API.
            builder.Services.AddSingleton<Plugins.PluginSupervisor>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<Plugins.PluginSupervisor>());

            var app = builder.Build();

            var metricServer = new MetricServer(port: 9090);
            metricServer.Start();

            app.MapGet("/health", () => Results.Ok("Healthy"));

            // Registry + status (roadmap Epic 1C).
            app.MapGet("/api/plugins", (Plugins.PluginSupervisor sv) => Results.Ok(new
            {
                host = sv.Host,
                plugins = sv.Plugins.Select(ToDto),
            }));

            app.MapGet("/api/plugins/{id}", (string id, Plugins.PluginSupervisor sv) =>
            {
                var e = sv.Get(id);
                return e is null ? Results.NotFound() : Results.Ok(ToDto(e));
            });

            app.MapPost("/api/plugins/{id}/start", (string id, Plugins.PluginSupervisor sv) =>
                Results.Ok(new { result = sv.Start(id) }));

            app.MapPost("/api/plugins/{id}/stop", (string id, Plugins.PluginSupervisor sv) =>
                Results.Ok(new { result = sv.Stop(id) }));

            app.Run();
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

    // Flatten an entry for the API (manifest + live supervision state, no Process handle).
    private static object ToDto(PluginEntry e) => new
    {
        id = e.Manifest.Id,
        name = e.Manifest.Name,
        version = e.Manifest.Version,
        description = e.Manifest.Description,
        kind = e.Manifest.Kind,
        providedCapabilities = e.Manifest.ProvidedCapabilities,
        resources = e.Manifest.Resources,
        autoStart = e.Manifest.AutoStart,
        status = e.Status,
        detail = e.Detail,
        restartCount = e.RestartCount,
        lastStartedAt = e.LastStartedAt,
        lastExitAt = e.LastExitAt,
    };
}
