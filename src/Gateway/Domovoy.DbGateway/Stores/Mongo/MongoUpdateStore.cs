// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Stores.Mongo;

/// <summary>
/// MongoDB implementation of <see cref="IUpdateStore"/> (roadmap Epic 3K over the Epic 3H seam).
/// Everything Mongo-specific about update settings lives here and nowhere else.
/// </summary>
public sealed class MongoUpdateStore : IUpdateStore
{
    public const string Collection = "update_settings";

    private readonly IMongoDatabase _db;

    public MongoUpdateStore(IMongoDatabase db) => _db = db;

    private IMongoCollection<UpdateSettings> Settings => _db.GetCollection<UpdateSettings>(Collection);

    public async Task<UpdateSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await Settings
            .Find(x => x.Id == UpdateSettings.SingletonId)
            .FirstOrDefaultAsync(ct);

        return settings ?? new UpdateSettings();
    }

    public Task SaveSettingsAsync(UpdateSettings settings, CancellationToken ct = default)
    {
        settings.Id = UpdateSettings.SingletonId;
        settings.UpdatedAt = DateTime.UtcNow;

        return Settings.ReplaceOneAsync(
            x => x.Id == UpdateSettings.SingletonId,
            settings,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public Task RecordCheckAsync(DateTime at, string result, CancellationToken ct = default)
    {
        // Точечное обновление, а не перезапись документа: проверка не должна затирать выбор
        // владельца, если он поменял канал одновременно с фоновым опросом реестра.
        var update = Builders<UpdateSettings>.Update
            .Set(x => x.LastCheckAt, at)
            .Set(x => x.LastCheckResult, result);

        return Settings.UpdateOneAsync(
            x => x.Id == UpdateSettings.SingletonId,
            update,
            new UpdateOptions { IsUpsert = true },
            ct);
    }
}
