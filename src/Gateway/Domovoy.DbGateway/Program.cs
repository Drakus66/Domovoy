using Domovoy.DbGateway.Config;
using Domovoy.DbGateway.Endpoints;
using Domovoy.MessageBus;

using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Configure MongoDB
var mongoDbSettings = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>()
                      ?? throw new InvalidOperationException("MongoDB settings are not configured");

builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(mongoDbSettings.ConnectionString));

builder.Services.AddSingleton<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbSettings.DatabaseName));

// Configure RabbitMQ
builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

// Add EventInterceptor as a hosted service
builder.Services.AddHostedService<Domovoy.DbGateway.Services.EventInterceptor>();

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
