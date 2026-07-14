// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Common.Configuration;
using Domovoy.CommutePlugin;
using Domovoy.CommutePlugin.Services;
using Domovoy.CommutePlugin.Traffic;
using Domovoy.MessageBus;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Roadmap Epic 2M — the Commute planner plugin's entry point. This is a plain worker process (Generic
// Host): the PluginSupervisor (Epic 1C) launches it as `dotnet Domovoy.CommutePlugin.dll` and it connects
// to the bus on its own, exactly like a device adapter. Config comes from COMMUTE__* / RABBITMQ__*
// environment set on the supervisor and inherited by this child process.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.Configure<CommuteOptions>(builder.Configuration.GetSection(CommuteOptions.SectionName));
builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();

// Typed client to the public API gateway (site location + geocoder, Epic 2K).
builder.Services.AddHttpClient<SettingsClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<CommuteOptions>>().Value;
    client.BaseAddress = new Uri(options.SettingsBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// HTTP client for the (optional) live TomTom backend; the planner builds the actual provider from the
// live plugin settings so switching simulator↔TomTom takes effect without a restart.
builder.Services.AddHttpClient("tomtom", c => c.Timeout = TimeSpan.FromSeconds(10));

// Plugin settings channel (Epic 2M tail): announces the schema and receives operator changes over the bus.
builder.Services.AddSingleton(sp => new PluginSettingsService(
    sp.GetRequiredService<IMessageBus>(),
    CommuteSettings.PluginId,
    CommuteSettings.Descriptors,
    sp.GetRequiredService<ILogger<PluginSettingsService>>()));

builder.Services.AddHostedService<CommutePlanner>();

await builder.Build().RunAsync();
