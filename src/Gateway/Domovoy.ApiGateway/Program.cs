namespace Domovoy.ApiGateway;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Prometheus;
using System.Text;
using Serilog;
using Domovoy.Common.Logging;

internal static class Program
{
    static async Task Main(string[] args)
    {
        SerilogBootstrap.Initialize("ApiGateway");

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
            });
            builder.Host.ConfigureSerilog();

            // Add configuration
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            // Add services to the container
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();

            // Authorization is deliberately deferred to Phase 2 (local auth "on top" of gateways/UI,
            // before locks & cameras). During development JWT is OFF by default so it doesn't get in
            // the way of testing — it stays wired behind a flag (JwtSettings:Enabled) rather than being
            // ripped out, so it can be switched back on without re-plumbing. See roadmap P0-6.
            var jwtSettings = builder.Configuration.GetSection("JwtSettings");
            var jwtEnabled = jwtSettings.GetValue<bool>("Enabled");

            // Configure Swagger
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Domovoy API Gateway", Version = "v1" });

                if (jwtEnabled)
                {
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
                }
            });

            if (jwtEnabled)
            {
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
            }

            // Capability device read-model lives in DbGateway; the gateway forwards reads to it
            // via CapabilityDevicesController (replaces the former Ocelot proxy route).
            var dbGatewayUrl = builder.Configuration["DbGateway:BaseUrl"] ?? "http://db-gateway:8080";
            builder.Services.AddHttpClient("db-gateway", client =>
            {
                client.BaseAddress = new Uri(dbGatewayUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            // AutomationService hosts the replay/simulation endpoint (roadmap Epic 1F); proxy to it.
            var automationUrl = builder.Configuration["AutomationService:BaseUrl"] ?? "http://automation-service:8080";
            builder.Services.AddHttpClient("automation-service", client =>
            {
                client.BaseAddress = new Uri(automationUrl);
                client.Timeout = TimeSpan.FromSeconds(30); // replay scans history; allow headroom
            });

            // PluginSupervisor hosts the plugin registry + lifecycle API (roadmap Epic 1C); proxy to it.
            var supervisorUrl = builder.Configuration["PluginSupervisor:BaseUrl"] ?? "http://plugin-supervisor:8080";
            builder.Services.AddHttpClient("plugin-supervisor", client =>
            {
                client.BaseAddress = new Uri(supervisorUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<MessageBus.IMessageBus, MessageBus.RabbitMqConnection>();
            builder.Services.AddHostedService<Services.EventRelayService>();
            builder.Services.AddSingleton<Services.ZigbeeBridgeStateCache>();
            builder.Services.AddHostedService<Services.ZigbeeBridgeCacheUpdater>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    policy => policy
                        .SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
            });

            builder.Services.AddHealthChecks();
            builder.Services.AddSingleton<MetricServer>(sp => new MetricServer(port: 9090));

            var prometheusUrl = builder.Configuration["Prometheus:BaseUrl"] ?? "http://prometheus:9090";
            builder.Services.AddHttpClient("prometheus", client =>
            {
                client.BaseAddress = new Uri(prometheusUrl);
                client.Timeout = TimeSpan.FromSeconds(5);
            });

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

            app.UseMiddleware<Middleware.RequestLoggingMiddleware>();
            app.UseMiddleware<Middleware.RequestCounterMiddleware>();
            app.UseMiddleware<Middleware.RouteCounterMiddleware>();

            // Start Prometheus
            var metricServer = app.Services.GetRequiredService<MetricServer>();
            metricServer.Start();

            if (jwtEnabled)
                app.UseAuthentication();
            app.UseAuthorization();

            // Uniform endpoint routing — every gateway responsibility is an in-process endpoint:
            // controllers (device-control, zigbee, metrics, status, capability-devices proxy),
            // the SignalR hub, and health checks.
            app.MapHealthChecks("/health");
            app.MapControllers();
            app.MapHub<Hubs.DeviceHub>("/hub/devices");

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
