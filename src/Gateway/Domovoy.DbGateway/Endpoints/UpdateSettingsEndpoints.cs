// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Stores;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Update settings (roadmap Epic 3K): which channel this house is subscribed to and how it polls.
/// <para>
/// The delivery service reads the channel from here — the database is the authoritative value, and
/// <c>.env</c> on the host is only a bootstrap fallback that the updater keeps in sync so a manual
/// <c>docker compose up -d</c> cannot roll the channel back.
/// </para>
/// <para>
/// <b>Storage-abstraction seam (Epic 3H):</b> a thin HTTP adapter — all persistence goes through
/// <see cref="IUpdateStore"/>, and no <c>MongoDB.Driver</c> type appears here.
/// </para>
/// </summary>
public static class UpdateSettingsEndpoints
{
    public record UpdateSettingsRequest(
        string? Channel, bool? CheckEnabled, int? CheckIntervalHours, bool? BackupBeforeUpdate);

    public static void MapUpdateSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Updates").WithOpenApi();

        group.MapGet("/update-settings", async (IUpdateStore store, CancellationToken ct) =>
            Results.Ok(await store.GetSettingsAsync(ct)));

        group.MapPut("/update-settings", async (
            UpdateSettingsRequest request, IUpdateStore store, CancellationToken ct) =>
        {
            var settings = await store.GetSettingsAsync(ct);

            if (request.Channel is not null)
            {
                if (!UpdateSettings.IsKnownChannel(request.Channel))
                    return Results.BadRequest(new
                    {
                        error = $"Неизвестный канал '{request.Channel}'. Допустимы " +
                                $"'{UpdateSettings.ChannelRelease}' и '{UpdateSettings.ChannelDev}'.",
                    });

                settings.Channel = request.Channel;
            }

            if (request.CheckEnabled is { } enabled) settings.CheckEnabled = enabled;
            if (request.BackupBeforeUpdate is { } backup) settings.BackupBeforeUpdate = backup;

            if (request.CheckIntervalHours is { } hours)
                settings.CheckIntervalHours = Math.Clamp(hours, 1, 24 * 7);

            await store.SaveSettingsAsync(settings, ct);
            return Results.Ok(settings);
        });

        // Отметка о фоновой проверке — пишет служба обновлений, не UI.
        group.MapPost("/update-settings/check-result", async (
            string result, IUpdateStore store, CancellationToken ct) =>
        {
            await store.RecordCheckAsync(DateTime.UtcNow, result, ct);
            return Results.Accepted();
        });
    }
}
