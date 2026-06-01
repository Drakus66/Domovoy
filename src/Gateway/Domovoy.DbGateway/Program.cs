namespace Domovoy.DbGateway;

using Config;
using Domovoy.DbGateway.Serializers;
using Endpoints;
using MessageBus;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Prometheus;
using Serilog;

using Domovoy.Common.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("DbGateway");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();

            // Explicitly configure URLs to listen on port 8080
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(8080);
            });

            // Configure MongoDB
            var mongoDbSettings = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>()
                                  ?? throw new InvalidOperationException("MongoDB settings are not configured");

            // Configure MongoDB serializer to handle JsonElement
            var objectSerializer = new ObjectSerializer();
            BsonSerializer.RegisterSerializer(objectSerializer);
            
            // Allow JsonElement serialization with custom serializer
            BsonSerializer.RegisterSerializer(new JsonElementSerializer());
            
            // Register custom dictionary serializer that can handle JsonElement values
            BsonSerializer.RegisterSerializer(new JsonObjectDictionarySerializer());

            builder.Services.AddSingleton<IMongoClient>(sp =>
                new MongoClient(mongoDbSettings.ConnectionString));

            builder.Services.AddSingleton<IMongoDatabase>(sp =>
                sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbSettings.DatabaseName));

            // Telemetry data-platform options (retention; roadmap Epic 1B).
            builder.Services.Configure<Config.TelemetryOptions>(
                builder.Configuration.GetSection(Config.TelemetryOptions.SectionName));

            // Configure RabbitMQ
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

            // Add EventInterceptor as a hosted service
            builder.Services.AddHostedService<Services.EventInterceptor>();

            // Add services to the container
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            // Prometheus: collect per-request HTTP metrics and expose them on /metrics (port 8080).
            app.UseHttpMetrics();

            // Map endpoints
            app.MapCapabilityDeviceEndpoints();
            app.MapZoneEndpoints();
            app.MapHistoryEndpoints();
            app.MapAutomationEndpoints();
            app.MapModeEndpoints();
            app.MapBlockEndpoints();
            app.MapMetrics();

            // Health check endpoint
            app.MapGet("/health", () => Results.Ok("Healthy"))
                .WithName("Health")
                .WithOpenApi();

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
