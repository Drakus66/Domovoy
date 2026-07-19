// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.PluginSupervisor;

using Configuration;
using Plugins;

using Domovoy.Common.Configuration;
using Domovoy.Common.Logging;
using Domovoy.Contracts.Plugins;
using Domovoy.MessageBus;

using Prometheus;
using Serilog;

internal static class Program
{
    static void Main(string[] args)
    {
        SerilogBootstrap.Initialize("PluginSupervisor");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

            builder.Services.Configure<SupervisorOptions>(
                builder.Configuration.GetSection(SupervisorOptions.SectionName));

            // Uploaded plugin packages (zip + binaries) can be tens of MB — lift the default request cap.
            builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 256 * 1024 * 1024);

            // The supervisor is both a hosted background service and an injectable for the API.
            builder.Services.AddSingleton<Plugins.PluginSupervisor>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<Plugins.PluginSupervisor>());

            // Bus connection for the plugin-settings channel (schema in, effective values out — applied live).
            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
            builder.Services.AddSystemControl("plugin-supervisor"); // UI-issued restart (self-stop → restart policy)
            builder.Services.AddSingleton<PluginSettingsRegistry>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<PluginSettingsRegistry>());

            var app = builder.Build();

            var metricServer = new MetricServer(port: 9090);
            metricServer.Start();

            app.MapGet("/health", () => Results.Ok("Healthy"));

            // Registry + status (roadmap Epic 1C).
            app.MapGet("/api/plugins", (Plugins.PluginSupervisor sv, PluginSettingsRegistry settings) => Results.Ok(new
            {
                host = sv.Host,
                plugins = sv.Plugins.Select(e => ToDto(e, settings)),
            }));

            app.MapGet("/api/plugins/{id}", (string id, Plugins.PluginSupervisor sv, PluginSettingsRegistry settings) =>
            {
                var e = sv.Get(id);
                return e is null ? Results.NotFound() : Results.Ok(ToDto(e, settings));
            });

            // Plugin settings (roadmap Epic 2M tail): the schema+values a plugin announced, and live updates.
            app.MapGet("/api/plugins/{id}/settings", (string id, PluginSettingsRegistry settings) =>
            {
                var view = settings.GetSettings(id);
                return view is null ? Results.NotFound(new { result = "no settings" }) : Results.Ok(view);
            });

            app.MapPut("/api/plugins/{id}/settings", (string id, PluginSettingsUpdate body, PluginSettingsRegistry settings) =>
            {
                var ok = settings.UpdateSettings(id, body.Values ?? new Dictionary<string, object?>());
                return ok ? Results.Ok(new { result = "applied" }) : Results.NotFound(new { result = "no settings" });
            });

            app.MapPost("/api/plugins/{id}/start", (string id, Plugins.PluginSupervisor sv) =>
                Results.Ok(new { result = sv.Start(id) }));

            app.MapPost("/api/plugins/{id}/stop", (string id, Plugins.PluginSupervisor sv) =>
                Results.Ok(new { result = sv.Stop(id) }));

            // Install a plugin from an uploaded .zip (manifest + binaries) — no container/volume access needed.
            // Accepts either a raw zip body or a multipart form field named "package".
            app.MapPost("/api/plugins/install", async (HttpRequest req, Plugins.PluginSupervisor sv, PluginSettingsRegistry settings, CancellationToken ct) =>
            {
                Stream? zip = req.Body;
                if (req.HasFormContentType)
                {
                    var form = await req.ReadFormAsync(ct);
                    zip = (form.Files.GetFile("package") ?? form.Files.FirstOrDefault())?.OpenReadStream();
                    if (zip is null) return Results.BadRequest(new { result = "no file field 'package' in the upload" });
                }

                var result = await sv.InstallAsync(zip, ct);
                var payload = new { result = result.Message, plugin = result.Entry is null ? null : ToDto(result.Entry, settings) };
                return result.Outcome switch
                {
                    InstallOutcome.Installed => Results.Ok(payload),
                    InstallOutcome.BadRequest => Results.BadRequest(payload),
                    _ => Results.Json(payload, statusCode: 500),
                };
            });

            // Uninstall: stop the process and remove its folder from the plugins root.
            app.MapDelete("/api/plugins/{id}", (string id, Plugins.PluginSupervisor sv) =>
            {
                var result = sv.Uninstall(id);
                return result == "not found" ? Results.NotFound(new { result }) : Results.Ok(new { result });
            });

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

    // Flatten an entry for the API (manifest + live supervision state, no Process handle).
    private static object ToDto(PluginEntry e, PluginSettingsRegistry settings) => new
    {
        id = e.Manifest.Id,
        name = e.Manifest.Name,
        version = e.Manifest.Version,
        description = e.Manifest.Description,
        kind = e.Manifest.Kind,
        providedCapabilities = e.Manifest.ProvidedCapabilities,
        resources = e.Manifest.Resources,
        autoStart = e.Manifest.AutoStart,
        status = e.Status,
        detail = e.Detail,
        restartCount = e.RestartCount,
        lastStartedAt = e.LastStartedAt,
        lastExitAt = e.LastExitAt,
        hasSettings = settings.HasSchema(e.Manifest.Id),
    };
}

/// <summary>Body of <c>PUT /api/plugins/{id}/settings</c> — the operator's chosen values keyed by setting key.</summary>
internal sealed record PluginSettingsUpdate(Dictionary<string, object?>? Values);
