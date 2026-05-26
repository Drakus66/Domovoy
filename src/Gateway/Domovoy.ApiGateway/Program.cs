namespace Domovoy.ApiGateway;

using Microsoft.Extensions.Configuration.Yaml;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Cache.CacheManager;
using Ocelot.Provider.Polly;
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
                .AddYamlFile("ocelot.yml", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();

            // Add services to the container
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

            // Configure JWT authentication
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

            // Configure Ocelot
            builder.Services
                .AddOcelot(builder.Configuration)
                .AddCacheManager(x => x.WithDictionaryHandle())
                .AddPolly();

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

            // Static files middleware removed - wwwroot not required

            app.UseMiddleware<Middleware.RequestLoggingMiddleware>();
            app.UseMiddleware<Middleware.RequestCounterMiddleware>();
            app.UseMiddleware<Middleware.RouteCounterMiddleware>();

            // Start Prometheus
            var metricServer = app.Services.GetRequiredService<MetricServer>();
            metricServer.Start();

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHealthChecks("/health");

            // Map endpoints BEFORE Ocelot — UseOcelot() is terminal middleware
            app.MapControllers();
            app.MapHub<Hubs.DeviceHub>("/hub/devices");

            await app.UseOcelot();

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
