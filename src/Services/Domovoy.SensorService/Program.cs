namespace Domovoy.SensorService;

using Services;
using MessageBus;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Domovoy.Common.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("SensorService");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();

            // Add services to the container
            builder.Services.AddOpenApi();
            builder.Services.AddHttpClient();
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
            builder.Services.AddSingleton<SensorManager>();

            var app = builder.Build();

            // Resolve SensorManager to start it
            app.Services.GetRequiredService<SensorManager>();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();
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
}
