using Domovoy.LightService.Services;
using Domovoy.MessageBus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
builder.Services.AddSingleton<LightManager>();

var app = builder.Build();

// Resolve LightManager to start it
app.Services.GetRequiredService<LightManager>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();