// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// The <c>backup_settings</c> singleton (Epic 3A), shared by the endpoints and the scheduler — same
/// get-or-default + upsert shape as the site-location/calendar settings.
/// </summary>
public static class BackupSettingsStore
{
    public const string Collection = "backup_settings";

    public static async Task<BackupSettings> GetOrDefaultAsync(IMongoDatabase db, CancellationToken ct = default)
    {
        var settings = await Settings(db)
            .Find(x => x.Id == BackupSettings.SingletonId)
            .FirstOrDefaultAsync(ct);
        return settings ?? new BackupSettings();
    }

    public static Task SaveAsync(IMongoDatabase db, BackupSettings settings, CancellationToken ct = default) =>
        Settings(db).ReplaceOneAsync(
            x => x.Id == BackupSettings.SingletonId, settings, new ReplaceOptions { IsUpsert = true }, ct);

    /// <summary>Stamp the outcome of a run (manual or scheduled) without touching the editable fields.</summary>
    public static Task RecordRunAsync(
        IMongoDatabase db, bool ok, string? file, string? error, CancellationToken ct = default)
    {
        var update = Builders<BackupSettings>.Update
            .Set(x => x.LastRunAt, DateTime.UtcNow)
            .Set(x => x.LastResult, ok ? "ok" : "error")
            .Set(x => x.LastError, error)
            .Set(x => x.LastFile, file);
        return Settings(db).UpdateOneAsync(
            x => x.Id == BackupSettings.SingletonId, update, new UpdateOptions { IsUpsert = true }, ct);
    }

    private static IMongoCollection<BackupSettings> Settings(IMongoDatabase db) =>
        db.GetCollection<BackupSettings>(Collection);
}
