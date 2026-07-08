// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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

            // Backstop that expires silent native devices (missed offline signal / stale-after-restart).
            builder.Services.AddHostedService<Services.DeviceLivenessWatchdog>();

            // Seed the built-in roles (admin/resident/guest) so the roles model is usable out of the box (Epic 2E).
            builder.Services.AddHostedService<Services.SecuritySeeder>();

            // House Diary (Epic 2N): the deterministic NLG renderer + locale selector + language-pack provider.
            // The deterministic Russian renderer is the mandatory default; an optional assisted (LLM/plugin)
            // renderer (Phase 4) can be registered as another INarrativeRenderer and the selector prefers it.
            builder.Services.AddSingleton<Domovoy.Narrative.INarrativeRenderer, Domovoy.Narrative.RuLanguagePackRenderer>();
            builder.Services.AddSingleton<Domovoy.Narrative.INarrativeRendererSelector, Domovoy.Narrative.NarrativeRendererSelector>();
            builder.Services.AddSingleton<Services.LanguagePackProvider>();

            // Optional geocoder for the site-location editor (Epic 2K). Network-only and best-effort — a failure
            // degrades to manual lat/lon entry, so the location feature stays fully usable offline.
            builder.Services.AddHttpClient<Services.IGeocoder, Services.NominatimGeocoder>(client =>
            {
                client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Domovoy/1.0 (home-automation)");
            });

            // Optional online public-holiday import for the Calendar sensor (Epic 2L). Best-effort — a
            // failure degrades to the hand-edited holiday list, keeping the calendar usable offline.
            builder.Services.AddHttpClient<Services.IHolidayImporter, Services.NagerHolidayImporter>(client =>
            {
                client.BaseAddress = new Uri("https://date.nager.at/");
                client.Timeout = TimeSpan.FromSeconds(8);
            });

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
            app.MapActivityEndpoints();
            app.MapHomeStoryEndpoints();
            app.MapMlEndpoints();
            app.MapProposalsEndpoints();
            app.MapRoleEndpoints();
            app.MapUserEndpoints();
            app.MapSettingsEndpoints();
            app.MapDashboardEndpoints();
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
