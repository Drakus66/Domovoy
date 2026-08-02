// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Updater.Model;
using Domovoy.Updater.Services;

namespace Domovoy.Updater.Endpoints;

/// <summary>
/// Internal HTTP surface of the delivery service (roadmap Epic 3K). Not exposed outside
/// <c>domovoy-network</c>: the WebUI reaches these through the api-gateway, where authentication and
/// the <c>system.admin</c> permission are already enforced.
/// </summary>
public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        // Что установлено и что доступно. Это же читает db-gateway, складывая версии в манифест бэкапа.
        app.MapGet("/api/components", async (UpdateCatalog catalog, CancellationToken ct) =>
        {
            var snapshot = await catalog.BuildAsync(ct);

            return Results.Ok(new
            {
                channel = snapshot.Channel,
                components = ReleaseSource.Components
                    .Append(ReleaseSource.TopologyComponent)
                    .Select(name =>
                    {
                        snapshot.Installed.TryGetValue(name, out var installed);
                        var newest = snapshot.Newest(name);
                        var hasUpdate = installed is not null && newest is not null
                                        && SemVerComparer.IsNewer(newest.Version, installed.Version);

                        return new
                        {
                            name,
                            installed = installed?.Version,
                            digest = installed?.Digest,
                            available = newest?.Version,
                            hasUpdate,
                            deps = installed?.Deps,
                        };
                    }),
            });
        });

        // Проверить прямо сейчас — кнопка «Проверить» в настройках.
        app.MapGet("/api/updates/check", async (UpdateCatalog catalog, CancellationToken ct) =>
        {
            var snapshot = await catalog.BuildAsync(ct);
            return Results.Ok(new
            {
                channel = snapshot.Channel,
                outdated = snapshot.Outdated,
                checkedAt = DateTimeOffset.UtcNow,
            });
        });

        // Что потянется за собой. Отдельный вызов, потому что диалог обновления обязан показать
        // это ДО того, как что-либо произойдёт: «вместе с ML обновится db-gateway, потому что…».
        app.MapPost("/api/updates/plan", async (
            PlanRequest request, UpdateCatalog catalog, CancellationToken ct) =>
        {
            var snapshot = await catalog.BuildAsync(ct);
            var resolver = new DependencyResolver(snapshot.Installed, snapshot.Available);

            var plan = request.All || request.Components is not { Count: > 0 }
                ? resolver.ResolveAll()
                : resolver.Resolve(request.Components);

            return Results.Ok(ToDto(plan, snapshot.Channel));
        });

        app.MapPost("/api/updates/apply", async (
            ApplyRequest request,
            UpdateCatalog catalog,
            UpdateExecutor executor,
            CancellationToken ct) =>
        {
            var snapshot = await catalog.BuildAsync(ct);
            var resolver = new DependencyResolver(snapshot.Installed, snapshot.Available);

            var plan = request.All || request.Components is not { Count: > 0 }
                ? resolver.ResolveAll()
                : resolver.Resolve(request.Components);

            if (!plan.Ok)
                return Results.Conflict(ToDto(plan, snapshot.Channel));

            if (plan.IsEmpty)
                return Results.Ok(new { status = "up-to-date" });

            // Обновление длится минуты и по дороге перезапускает api-gateway и webui, поэтому
            // отвечаем сразу, а ход выполнения UI дочитывает из /api/updates/status.
            _ = Task.Run(() => executor.ApplyAsync(plan, snapshot, request.Backup, CancellationToken.None));

            return Results.Accepted("/api/updates/status", new { status = "started", plan = ToDto(plan, snapshot.Channel) });
        });

        app.MapGet("/api/updates/status", (UpdateExecutor executor) =>
        {
            var run = executor.GetCurrentRun();
            return run is null ? Results.Ok(new { status = "idle" }) : Results.Ok(run);
        });

        app.MapGet("/api/updates/history", (UpdateExecutor executor) =>
            Results.Ok(executor.GetHistory().Reverse()));

        app.MapPost("/api/updates/rollback", async (UpdateExecutor executor, CancellationToken ct) =>
        {
            _ = Task.Run(() => executor.RollbackAsync(CancellationToken.None));
            await Task.CompletedTask;
            return Results.Accepted("/api/updates/status", new { status = "started" });
        });
    }

    private static object ToDto(UpdatePlan plan, string channel) => new
    {
        channel,
        ok = plan.Ok,
        refusal = plan.Refusal,
        conflict = plan.Conflict,
        empty = plan.IsEmpty,
        groups = plan.Groups.Select(g => new
        {
            atomic = g.Atomic,
            members = g.Members.Select(m => new
            {
                component = m.Component,
                container = m.Container,
                from = m.FromVersion,
                to = m.ToVersion,
                reason = m.Reason,
            }),
        }),
    };

    public sealed record PlanRequest(List<string>? Components, bool All = false);

    public sealed record ApplyRequest(List<string>? Components, bool All = false, bool Backup = true);
}
