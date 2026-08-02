// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Updater;

using Domovoy.Common.Configuration;
using Domovoy.Common.Logging;
using Domovoy.MessageBus;
using Domovoy.Updater.Configuration;
using Domovoy.Updater.Endpoints;
using Domovoy.Updater.Services;

using Serilog;

/// <summary>
/// Delivery and self-update service (roadmap Epic 3K).
///
/// <para>Publishes no ports outside <c>domovoy-network</c>: the WebUI reaches it through the
/// api-gateway, which is where authorization already lives. It holds the Docker socket and write
/// access to the installation directory — that privilege sits here, in one narrow service, rather
/// than on the internet-facing gateway.</para>
/// </summary>
internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var options = BindOptionsFromEnvironment();

        // Режим одноразового агента: заменить работающий контейнер службы обновлений на новый.
        // Обрабатывается до построения хоста — агенту не нужны ни шина, ни HTTP.
        var selfReplaceIndex = Array.IndexOf(args, SelfReplaceAgent.Switch);
        if (selfReplaceIndex >= 0 && selfReplaceIndex + 1 < args.Length)
            return await SelfReplaceAgent.RunAsync(args[selfReplaceIndex + 1], options, CancellationToken.None);

        SerilogBootstrap.Initialize("Updater");

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.ConfigureSerilog();
            builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(8080));

            builder.Services.Configure<UpdaterOptions>(builder.Configuration.GetSection(UpdaterOptions.Section));
            builder.Services.AddSingleton(sp =>
            {
                var bound = new UpdaterOptions();
                builder.Configuration.GetSection(UpdaterOptions.Section).Bind(bound);
                return bound;
            });

            builder.Services.AddHttpClient();
            builder.Services.AddHttpClient<RegistryClient>(c => c.Timeout = TimeSpan.FromSeconds(30));

            builder.Services.AddSingleton<DockerInventory>();
            builder.Services.AddSingleton<TopologyManager>();
            builder.Services.AddSingleton<ComposeRunner>();
            builder.Services.AddSingleton<UpdateCatalog>();
            builder.Services.AddSingleton<UpdateExecutor>();

            // Шов доверия: v1 принимает набор, пришедший из встроенного источника. Подписанный
            // релиз-манифест — вторая реализация этого же интерфейса, без правок вокруг.
            builder.Services.AddSingleton<IReleaseTrustPolicy, ConstantSourceTrustPolicy>();

            builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
            builder.Services.AddSingleton<IMessageBus, RabbitMqConnection>();
            builder.Services.AddSystemControl("domovoy-updater"); // перезапуск из UI, как у остальных служб

            builder.Services.AddHostedService<UpdateCheckService>();

            var app = builder.Build();

            app.MapGet("/health", () => Results.Ok("Healthy"));
            app.MapUpdateEndpoints();

            Log.Information(
                "Служба обновлений запущена. Источник: {Image}, каталог установки: {Directory}",
                ReleaseSource.ImageOf("<component>"), options.InstallDirectory);

            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Служба обновлений завершилась аварийно");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Reads options straight from the environment for the self-replace mode, which runs before the
    /// generic host (and its configuration pipeline) exists.
    /// </summary>
    private static UpdaterOptions BindOptionsFromEnvironment()
    {
        var options = new UpdaterOptions();

        if (Environment.GetEnvironmentVariable("UPDATES__INSTALLDIRECTORY") is { Length: > 0 } dir)
            options.InstallDirectory = dir;

        if (Environment.GetEnvironmentVariable("UPDATES__DBGATEWAYBASEURL") is { Length: > 0 } url)
            options.DbGatewayBaseUrl = url;

        if (Environment.GetEnvironmentVariable("UPDATES__DOCKERSOCKET") is { Length: > 0 } socket)
            options.DockerSocket = socket;

        return options;
    }
}
