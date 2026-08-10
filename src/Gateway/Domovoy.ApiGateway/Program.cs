// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.ApiGateway;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Domovoy.ApiGateway.Services;
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

            // Local auth (mobile-app / remote-access track). JWT is gated behind JwtSettings:Enabled: OFF in dev /
            // integration tests (the API stays open, exactly as before), ON in the compose/production posture that
            // gets exposed externally. AuthController mints the tokens; the DbGateway verifies credentials.
            var jwtSettings = builder.Configuration.GetSection("JwtSettings");
            var jwtEnabled = jwtSettings.GetValue<bool>("Enabled");
            builder.Services.AddSingleton(new Services.AuthEnforcementOptions { Enabled = jwtEnabled });
            builder.Services.AddSingleton<Services.JwtTokenService>();
            builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Services.PermissionAuthorizationHandler>();

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
                // Падает на старте, а не на первом входе: неверная настройка безопасности должна быть
                // видна сразу и в журнале, а не проявляться подписью общеизвестным секретом.
                var secretKey = Services.JwtTokenService.RequireSecret(builder.Configuration);
                var issuer = jwtSettings["Issuer"] ?? "domovoy";
                var audience = jwtSettings["Audience"] ?? "domovoy-clients";

                builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    // Short claim names are used verbatim (JwtTokenService writes sub/uname/role/perm).
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                        NameClaimType = Services.JwtTokenService.NameClaim,
                        RoleClaimType = Services.JwtTokenService.RoleClaim,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };

                    // SignalR can't set an Authorization header on the WebSocket; the JS client passes the token as
                    // the access_token query arg on /hub/*. Lift it into the request for those paths only, and never
                    // let it reach the request log (RequestLoggingMiddleware) beyond the query string it already is.
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            var path = context.HttpContext.Request.Path;
                            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                                context.Token = accessToken;
                            return Task.CompletedTask;
                        }
                    };
                });
            }

            builder.Services.AddAuthorization(options =>
            {
                // One policy per permission (named as the permission string) for [Authorize(Policy = ...)].
                options.AddPermissionPolicies();

                // When enforcement is on, every endpoint requires an authenticated user unless it opts out with
                // [AllowAnonymous] (login/refresh) — that's the blanket gate that closes the whole API. Off in dev.
                if (jwtEnabled)
                    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build();
            });

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
                // Replay scans history; ML training (2P) pages telemetry + fits several templates per scope —
                // a multi-zone train run takes tens of seconds, so give the proxy real headroom.
                client.Timeout = TimeSpan.FromSeconds(180);
            });

            // Delivery service hosts the update API (roadmap Epic 3K); proxy to it. It publishes no ports
            // outside domovoy-network, so this proxy — with its auth and system.admin policy — is the only
            // way to reach it.
            var updaterUrl = builder.Configuration["Updater:BaseUrl"] ?? "http://domovoy-updater:8080";
            builder.Services.AddHttpClient("updater", client =>
            {
                client.BaseAddress = new Uri(updaterUrl);
                // Checking the channel walks every component's manifest in the registry; over a slow
                // home connection that is seconds, not milliseconds.
                client.Timeout = TimeSpan.FromSeconds(120);
            });

            // PluginSupervisor hosts the plugin registry + lifecycle API (roadmap Epic 1C); proxy to it.
            var supervisorUrl = builder.Configuration["PluginSupervisor:BaseUrl"] ?? "http://plugin-supervisor:8080";
            builder.Services.AddHttpClient("plugin-supervisor", client =>
            {
                client.BaseAddress = new Uri(supervisorUrl);
                // Install extracts + places + restarts a plugin; give it more headroom than a status call.
                client.Timeout = TimeSpan.FromSeconds(120);
            });

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<MessageBus.IMessageBus, MessageBus.RabbitMqConnection>();
            MessageBus.SystemControlExtensions.AddSystemControl(builder.Services, "api-gateway"); // UI-issued restart

            // System control (restart via UI). Self-restart over the bus is always available; the opt-in Docker
            // path (SystemControl:DockerEnabled + a mounted docker.sock) can also reach infra/hung containers.
            builder.Services.Configure<Services.SystemControlOptions>(
                builder.Configuration.GetSection(Services.SystemControlOptions.Section));
            var systemOptions = builder.Configuration.GetSection(Services.SystemControlOptions.Section)
                .Get<Services.SystemControlOptions>() ?? new Services.SystemControlOptions();
            if (systemOptions.DockerEnabled)
                builder.Services.AddSingleton<Services.IContainerControl>(sp =>
                    new Services.DockerContainerControl(systemOptions, sp.GetRequiredService<ILogger<Services.DockerContainerControl>>()));
            else
                builder.Services.AddSingleton<Services.IContainerControl, Services.DisabledContainerControl>();
            builder.Services.AddHostedService<Services.EventRelayService>();
            builder.Services.AddHostedService<Services.NotificationRelayService>(); // 2M.2 LAN notification banner
            builder.Services.AddSingleton<Services.ZigbeeBridgeStateCache>();
            builder.Services.AddHostedService<Services.ZigbeeBridgeCacheUpdater>();

            // CORS: when Cors:AllowedOrigins is configured (the exposed compose/production posture), restrict to
            // that allowlist so a hostile page can't ride a logged-in session's credentials. With no list (dev),
            // fall back to reflecting any origin — the previous permissive behaviour, but only in dev.
            var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                              ?? Array.Empty<string>();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", policy =>
                {
                    if (corsOrigins.Length > 0)
                        policy.WithOrigins(corsOrigins);
                    else
                        policy.SetIsOriginAllowed(_ => true);
                    policy.AllowAnyMethod().AllowAnyHeader().AllowCredentials();
                });
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

            // Prometheus по шаблону маршрута (как в DbGateway). Прежняя пара самописных middleware
            // клеила метки из СЫРОГО пути — с GUID устройств и именами файлов бэкапов внутри, то есть
            // выдавала новый временной ряд на каждое устройство и каждый бэкап. Это бомба кардинальности:
            // ряды в Prometheus не истекают, память растёт молча. Заодно снято дублирование смысла:
            // счётчик, гистограмма и «активные запросы» были расписаны дважды в двух middleware.
            app.UseHttpMetrics();

            // Start Prometheus
            var metricServer = app.Services.GetRequiredService<MetricServer>();
            metricServer.Start();

            if (jwtEnabled)
                app.UseAuthentication();
            app.UseAuthorization();

            // Uniform endpoint routing — every gateway responsibility is an in-process endpoint:
            // controllers (device-control, zigbee, metrics, status, capability-devices proxy),
            // the SignalR hub, and health checks.
            // Health must stay reachable without a token — container probes and the app's connection-manager
            // (LAN→IPv6→SSH) hit it to pick a transport before there is any session.
            app.MapHealthChecks("/health").AllowAnonymous();
            app.MapControllers();
            app.MapHub<Hubs.DeviceHub>("/hub/devices");
            // 2M.2: notifications ride their own hub so banner clients don't receive the device-state firehose.
            app.MapHub<Hubs.NotificationHub>("/hub/notifications");

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
