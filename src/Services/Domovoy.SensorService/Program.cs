using Domovoy.SensorService.Services;
using Domovoy.MessageBus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
builder.Services.AddSingleton<SensorManager>();

var app = builder.Build();

// Resolve SensorManager to start it
app.Services.GetRequiredService<SensorManager>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();