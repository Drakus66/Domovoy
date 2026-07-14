// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Domovoy.Common.Logging;

public static class SerilogBootstrap
{
    /// <summary>Shared collection for operational logs queried by the Activity Center (roadmap Epic 2G).</summary>
    public const string OpsLogCollection = "ops_logs";

    public static void Initialize(string serviceName = "Domovoy.Service")
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(
                theme: AnsiConsoleTheme.Code,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}");

        // Operational-log Mongo sink (Epic 2G): if SERILOG_MONGO_URI is set (mongodb://host:port/DomovoyDb),
        // also write ops logs to a capped `ops_logs` collection so the Activity Center can search system logs.
        // Domain event-log stays separate (P0-5); these are merged only at the query/UI layer. Best-effort —
        // a sink failure must never prevent the service from logging to console.
        var mongoUri = Environment.GetEnvironmentVariable("SERILOG_MONGO_URI");
        if (!string.IsNullOrWhiteSpace(mongoUri))
        {
            try
            {
                config = config.WriteTo.MongoDBBson(cfg =>
                {
                    cfg.SetConnectionString(mongoUri);
                    cfg.SetCollectionName(OpsLogCollection);
                    cfg.SetCreateCappedCollection(cappedMaxSizeMb: 100, cappedMaxDocuments: 200_000); // bound size, no manual TTL
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SerilogBootstrap] Mongo sink disabled: {ex.Message}");
            }
        }

        Log.Logger = config.CreateLogger();
    }

    public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog();
    }

    public static IServiceCollection ConfigureSerilog(this IServiceCollection services)
    {
        return services.AddSerilog();
    }
}
