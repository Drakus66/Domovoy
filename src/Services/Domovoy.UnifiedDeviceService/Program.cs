// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.UnifiedDeviceService;

using Services;
using MessageBus;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;
using Serilog;
using Domovoy.Common.Logging;

internal static class Program
{
    static void  Main(string[] args)
    {
        SerilogBootstrap.Initialize("UnifiedDeviceService");

        try
        {
            var hostBuilder = Host.CreateDefaultBuilder(args);
            hostBuilder.ConfigureSerilog();
            hostBuilder.ConfigureServices((context, services) =>
            {
                // Configure RabbitMQ
                services.Configure<RabbitMqConfig>(context.Configuration.GetSection("RabbitMQ"));

                // Register message bus
                services.AddSingleton<IMessageBus, RabbitMqConnection>();

                // Capability-contract consumer — sole device-management path after roadmap Step 5.
                services.AddHostedService<CapabilityDeviceManager>();
            });

            var host = hostBuilder.Build();

            // Generic host has no Kestrel of its own — expose Prometheus metrics on a standalone
            // server (:9090) so Prometheus can scrape this service like the others.
            var metricServer = new MetricServer(port: 9090);
            metricServer.Start();

            host.Run();
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
