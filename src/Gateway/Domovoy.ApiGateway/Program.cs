using Microsoft.Extensions.Configuration.Yaml;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Cache.CacheManager;
using Ocelot.Provider.Polly;

using Prometheus; // prometheus-net - используется для метрик
// для health checks

using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddYamlFile("ocelot.yml", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add controllers and API explorer for Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Domovoy API Gateway", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure JWT authentication (replace with your actual settings)
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? "DefaultDevelopmentSecretKeyThatShouldBeReplacedInProduction";
var issuer = jwtSettings["Issuer"] ?? "domovoy";
var audience = jwtSettings["Audience"] ?? "domovoy-clients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Configure Ocelot with additional features
builder.Services
    .AddOcelot(builder.Configuration)
    .AddCacheManager(x =>
    {
        x.WithDictionaryHandle();
    })
    .AddPolly(); // Adds circuit breaker and retry policies

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add Message Bus for EventRelayService
builder.Services.AddSingleton<Domovoy.MessageBus.IMessageBus, Domovoy.MessageBus.RabbitMqConnection>();

// Add EventRelayService as hosted service
builder.Services.AddHostedService<Domovoy.ApiGateway.Services.EventRelayService>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// Configure Health Check
builder.Services.AddHealthChecks();

// Добавление поддержки Prometheus
builder.Services.AddSingleton<MetricServer>(sp => new MetricServer(port: 9090));

// Add custom middleware for metrics (will be registered in the pipeline directly)
// No need to register as singletons, they're instantiated in pipeline

// Add application insights if configuration exists
if (!string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("CorsPolicy");

// Add our custom request logging and metrics middleware
app.UseMiddleware<Domovoy.ApiGateway.Middleware.RequestLoggingMiddleware>();
app.UseMiddleware<Domovoy.ApiGateway.Middleware.RequestCounterMiddleware>();
app.UseMiddleware<Domovoy.ApiGateway.Middleware.RouteCounterMiddleware>();

// Запуск Prometheus metric server
var metricServer = app.Services.GetRequiredService<MetricServer>();
metricServer.Start();

app.UseAuthentication();
app.UseAuthorization();

// Map health check endpoint
app.MapHealthChecks("/health");

// Use Ocelot middleware
await app.UseOcelot();

app.MapControllers();

// Map SignalR DeviceHub for real-time updates
app.MapHub<Domovoy.ApiGateway.Hubs.DeviceHub>("/hub/devices");

app.Run();
