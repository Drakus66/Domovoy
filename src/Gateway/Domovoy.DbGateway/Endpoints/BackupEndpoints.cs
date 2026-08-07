// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Messaging;
using Domovoy.DbGateway.Services;
using Domovoy.MessageBus;

using Microsoft.AspNetCore.Http.Features;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Backup / restore / migration as a product (roadmap Epic 3A): schedule + retention settings
/// (<c>backup_settings</c> singleton), the bundle list, manual runs, download/upload (host migration) and
/// in-place restore. After a restore the whole stack is restarted over the bus (the Epic "system restart"
/// mechanics) so every service reloads its persisted state — the product form of "stop stack → restore →
/// start".
/// </summary>
public static class BackupEndpoints
{
    public record BackupSettingsUpdate(bool Enabled, string Time, int KeepCount);

    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backup").WithTags("Backup").WithOpenApi();

        group.MapGet("/settings", async (IMongoDatabase db) =>
            Results.Ok(await BackupSettingsStore.GetOrDefaultAsync(db)));

        group.MapPut("/settings", async (BackupSettingsUpdate body, IMongoDatabase db) =>
        {
            if (!TimeOnly.TryParseExact(body.Time, "HH:mm", out _))
                return Results.BadRequest(new { error = "time must be HH:mm" });
            if (body.KeepCount is < 1 or > 365)
                return Results.BadRequest(new { error = "keepCount must be between 1 and 365" });

            // Only the editable fields — the Last* status stamps belong to the runs.
            var settings = await BackupSettingsStore.GetOrDefaultAsync(db);
            settings.Enabled = body.Enabled;
            settings.Time = body.Time;
            settings.KeepCount = body.KeepCount;
            settings.UpdatedAt = DateTime.UtcNow;

            await BackupSettingsStore.SaveAsync(db, settings);
            return Results.Ok(settings);
        });

        group.MapGet("/", (BackupService backup) => Results.Ok(backup.List()));

        // ?reason= is recorded in the bundle manifest, so "why does this backup exist" survives in the
        // artefact itself. The delivery service (Epic 3K) passes `pre-update`; the UI passes nothing.
        group.MapPost("/run", (string? reason, BackupService backup, IMongoDatabase db, CancellationToken ct) =>
            Guarded(async () =>
            {
                try
                {
                    var result = await backup.CreateBackupAsync(NormalizeReason(reason), ct);
                    await BackupSettingsStore.RecordRunAsync(db, ok: true, file: result.FileName, error: null, ct);

                    var settings = await BackupSettingsStore.GetOrDefaultAsync(db, ct);
                    backup.ApplyRetention(settings.KeepCount);

                    return Results.Ok(new
                    {
                        file = result.FileName,
                        sizeBytes = result.SizeBytes,
                        collections = result.Manifest.Collections.Count,
                        documents = result.Manifest.Collections.Sum(c => c.Documents),
                    });
                }
                catch (Exception ex) when (ex is not BackupBusyException and not OperationCanceledException)
                {
                    await BackupSettingsStore.RecordRunAsync(db, ok: false, file: null, error: ex.Message, ct);
                    throw;
                }
            }));

        group.MapGet("/{file}/download", (string file, BackupService backup) =>
            Guarded(() =>
            {
                var path = backup.ResolveExistingFile(file);
                return Task.FromResult(Results.File(path, "application/zip", fileDownloadName: file));
            }));

        group.MapDelete("/{file}", (string file, BackupService backup) =>
            Guarded(() =>
            {
                backup.Delete(file);
                return Task.FromResult(Results.Ok(new { deleted = file }));
            }));

        // In-place restore; ?restart=false skips the stack restart (tests / expert use).
        group.MapPost("/{file}/restore", (string file, bool? restart, BackupService backup, IMessageBus bus,
                ILogger<BackupService> logger, CancellationToken ct) =>
            Guarded(async () =>
            {
                var result = await backup.RestoreBackupAsync(file, ct);

                var restarting = restart != false;
                if (restarting)
                {
                    // The listener's grace delay lets this response reach the client before services stop.
                    await PublishRestartAllAsync(bus);
                    logger.LogWarning("Stack restart broadcast after restoring {File}", file);
                }

                return Results.Ok(new
                {
                    restored = file,
                    collections = result.Collections,
                    documents = result.Documents,
                    pluginSettings = result.PluginSettingsRestored,
                    extrasStagingDirectory = result.ExtrasStagingDirectory,
                    restarting,
                });
            }));

        // Host migration: accept a bundle as the raw request body (application/zip).
        group.MapPost("/upload", (HttpRequest request, BackupService backup, CancellationToken ct) =>
            Guarded(async () =>
            {
                var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                    sizeFeature.MaxRequestBodySize = null; // bundles easily exceed the 30 MB default

                var file = await backup.SaveUploadAsync(request.Body, ct);
                return Results.Ok(new { file });
            }));
    }

    /// <summary>
    /// A caller-supplied reason ends up verbatim in the manifest, so keep it short and printable;
    /// absent or blank means the default manual run.
    /// </summary>
    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "manual";
        var trimmed = reason.Trim();
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    /// <summary>Translate the backup exceptions into their HTTP shapes (409 busy / 404 / 400).</summary>
    private static async Task<IResult> Guarded(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BackupBusyException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (BackupNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (BackupFormatException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static Task PublishRestartAllAsync(IMessageBus bus)
    {
        var envelope = Envelope<SystemControlV1>.Create(
            MessageTypes.SystemControl,
            source: "dbgateway/backup",
            data: new SystemControlV1(SystemControlListener.AllTarget, SystemControlListener.RestartAction),
            subject: SystemControlListener.AllTarget);
        return bus.PublishAsync(BusTopology.EventsExchange, BusTopology.SystemControlKey, envelope);
    }
}
