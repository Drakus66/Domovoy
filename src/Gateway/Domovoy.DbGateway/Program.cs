namespace Domovoy.DbGateway;

using Config;
using Domovoy.DbGateway.Serializers;
using Domovoy.DbGateway.Repositories;
using Endpoints;
using MessageBus;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Serilog;

using Domovoy.Common.Logging;
using Domovoy.DbGateway.Models;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("DbGateway");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();

            // Configure MongoDB
            var mongoDbSettings = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>()
                                  ?? throw new InvalidOperationException("MongoDB settings are not configured");

            // Configure MongoDB serializer to handle JsonElement
            var objectSerializer = new ObjectSerializer();
            BsonSerializer.RegisterSerializer(objectSerializer);
            
            // Allow JsonElement serialization with custom serializer
            BsonSerializer.RegisterSerializer(new JsonElementSerializer());

            builder.Services.AddSingleton<IMongoClient>(sp =>
                new MongoClient(mongoDbSettings.ConnectionString));

            builder.Services.AddSingleton<IMongoDatabase>(sp =>
                sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbSettings.DatabaseName));

            // Configure RabbitMQ
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

            // Add EventInterceptor as a hosted service
            builder.Services.AddHostedService<Services.EventInterceptor>();

            // Register repositories
            builder.Services.AddSingleton<IBaseRepository<Device>, DeviceRepository>();
            builder.Services.AddSingleton<IDeviceRepository, DeviceRepository>();

            // Add services to the container
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            // Map endpoints
            app.MapDeviceEndpoints();

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
