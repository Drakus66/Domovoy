// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Connectivity;

using Services;
using MessageBus;
using Adapters;
using Prometheus;
using Serilog;

using Domovoy.Common.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("ConnectivityService");

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.ConfigureSerilog();

            // Configure Message Bus (RabbitMQ)
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMq"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
            builder.Services.AddSystemControl("connectivity-service"); // UI-issued restart (self-stop → restart policy)

            // Zigbee bridge state cache (shared between adapter and HTTP endpoint)
            builder.Services.AddSingleton<ZigbeeBridgeCache>();

            // Register Adapters
            builder.Services.AddSingleton<IProtocolAdapter, DomovoyNativeAdapter>();
            builder.Services.AddSingleton<IProtocolAdapter, Zigbee2MqttAdapter>();
            builder.Services.AddSingleton<IProtocolAdapter, EspHomeMqttAdapter>(); // Epic 2J: ESP32/ESP8266 via ESPHome + MQTT

            // Add Adapter Manager (Connectivity Service)
            builder.Services.AddHostedService<AdapterManager>();

            var host = builder.Build();

            // Worker host has no Kestrel of its own — expose Prometheus metrics on a standalone
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
