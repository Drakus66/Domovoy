// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Config;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Epic 3A backup/restore against a <b>real</b> MongoDB: a bundle round-trip must bring back both plain
/// and time-series collections (the latter re-created with their time-series options — an implicit insert
/// would silently make a plain collection and break retention/rollups), carry plugin settings, exclude the
/// capped ops-log, and enforce the keep-N retention.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class BackupServiceTests
{
    private readonly InfraFixture _fx;

    public BackupServiceTests(InfraFixture fx) => _fx = fx;

    [Fact]
    public async Task Backup_then_restore_round_trips_plain_and_timeseries_collections()
    {
        var db = _fx.Db;
        await TimeSeriesInitializer.EnsureCollectionsAsync(db, NullLogger.Instance, 0);

        // Seed a plain collection, the settings singleton, telemetry samples and an ops_logs decoy.
        var zones = db.GetCollection<BsonDocument>("zones");
        await zones.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        await zones.InsertOneAsync(new BsonDocument { { "_id", "zone-1" }, { "Name", "Kitchen" } });

        await BackupSettingsStore.SaveAsync(db, new BackupSettings { Enabled = true, Time = "04:00", KeepCount = 3 });

        var readings = db.GetCollection<BsonDocument>(TimeSeriesInitializer.SensorReadingsCollection);
        var t0 = DateTime.UtcNow;
        await readings.InsertManyAsync(Enumerable.Range(0, 3).Select(i => new BsonDocument
        {
            { "Timestamp", t0.AddSeconds(i) },
            { "Meta", new BsonDocument { { "DeviceId", "backup-dev" }, { "CapabilityId", "temperature" } } },
            { "Value", 20.0 + i },
        }));
        var readingsBefore = await readings.CountDocumentsAsync(
            new BsonDocument("Meta.DeviceId", "backup-dev"));

        await db.GetCollection<BsonDocument>("ops_logs").InsertOneAsync(new BsonDocument { { "msg", "noise" } });

        // Plugin settings directory (Epic 1C shape: flat {id}.json files).
        var pluginDir = Path.Combine(Path.GetTempPath(), "domovoy-backup-test-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "demo-plugin.json"), """{"values":{"speed":1}}""");

        var backupDir = NewTempDir();
        var service = NewService(backupDir, pluginDir);
        try
        {
            var run = await service.CreateBackupAsync("manual");

            Assert.True(File.Exists(Path.Combine(backupDir, run.FileName)));
            Assert.Contains(run.Manifest.Collections, c => c.Name == "zones");
            Assert.Contains(run.Manifest.Collections, c => c.Name == BackupSettingsStore.Collection);
            Assert.Contains("demo-plugin.json", run.Manifest.PluginSettings);
            Assert.DoesNotContain(run.Manifest.Collections, c => c.Name == "ops_logs");

            var tsEntry = run.Manifest.Collections.Single(
                c => c.Name == TimeSeriesInitializer.SensorReadingsCollection);
            Assert.NotNull(tsEntry.TimeSeries);
            Assert.Equal("Timestamp", tsEntry.TimeSeries!.TimeField);
            Assert.Equal("Meta", tsEntry.TimeSeries.MetaField);

            // Wreck the state: drop the time-series collection, poison the plain one, lose the settings file.
            await db.DropCollectionAsync(TimeSeriesInitializer.SensorReadingsCollection);
            await zones.InsertOneAsync(new BsonDocument { { "_id", "zone-evil" }, { "Name", "Should vanish" } });
            File.Delete(Path.Combine(pluginDir, "demo-plugin.json"));

            var restore = await service.RestoreBackupAsync(run.FileName);

            Assert.Equal(run.Manifest.Collections.Count, restore.Collections);
            Assert.Equal(1, restore.PluginSettingsRestored);
            Assert.True(File.Exists(Path.Combine(pluginDir, "demo-plugin.json")));

            // Plain collection is back to the snapshot: seeded doc present, poison gone.
            Assert.NotNull(await zones.Find(new BsonDocument("_id", "zone-1")).FirstOrDefaultAsync());
            Assert.Null(await zones.Find(new BsonDocument("_id", "zone-evil")).FirstOrDefaultAsync());

            // Time-series collection is re-created AS time-series with the same documents.
            var infos = await (await db.ListCollectionsAsync()).ToListAsync();
            var restored = infos.Single(
                d => d["name"] == TimeSeriesInitializer.SensorReadingsCollection);
            Assert.Equal("timeseries", restored["type"].AsString);
            Assert.Equal(readingsBefore, await readings.CountDocumentsAsync(
                new BsonDocument("Meta.DeviceId", "backup-dev")));

            // The settings singleton round-tripped through the bundle too.
            var settings = await BackupSettingsStore.GetOrDefaultAsync(db);
            Assert.Equal("04:00", settings.Time);
            Assert.Equal(3, settings.KeepCount);
        }
        finally
        {
            Directory.Delete(backupDir, recursive: true);
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retention_keeps_only_the_newest_bundles()
    {
        var backupDir = NewTempDir();
        try
        {
            foreach (var stamp in new[] { "20260101-010000", "20260102-010000", "20260103-010000", "20260104-010000" })
                await File.WriteAllBytesAsync(
                    Path.Combine(backupDir, $"{BackupService.FilePrefix}{stamp}.zip"), new byte[] { 1 });

            var service = NewService(backupDir, pluginDir: null);
            var deleted = service.ApplyRetention(2);

            Assert.Equal(2, deleted);
            var left = Directory.GetFiles(backupDir).Select(Path.GetFileName).OrderBy(x => x).ToList();
            Assert.Equal(
                new[] { "domovoy-backup-20260103-010000.zip", "domovoy-backup-20260104-010000.zip" }, left);
        }
        finally
        {
            Directory.Delete(backupDir, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_rejects_a_non_bundle_and_leaves_nothing_behind()
    {
        var backupDir = NewTempDir();
        try
        {
            var service = NewService(backupDir, pluginDir: null);
            using var garbage = new MemoryStream(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            await Assert.ThrowsAsync<BackupFormatException>(() => service.SaveUploadAsync(garbage));
            Assert.Empty(Directory.GetFiles(backupDir));
        }
        finally
        {
            Directory.Delete(backupDir, recursive: true);
        }
    }

    [Fact]
    public void Illegal_file_names_are_rejected()
    {
        var backupDir = NewTempDir();
        try
        {
            var service = NewService(backupDir, pluginDir: null);
            Assert.Throws<BackupFormatException>(() => service.ResolveExistingFile("../../etc/passwd"));
            Assert.Throws<BackupFormatException>(() => service.ResolveExistingFile("random.zip"));
            Assert.Throws<BackupNotFoundException>(
                () => service.ResolveExistingFile("domovoy-backup-20990101-000000.zip"));
        }
        finally
        {
            Directory.Delete(backupDir, recursive: true);
        }
    }

    private BackupService NewService(string backupDir, string? pluginDir) =>
        new(_fx.Db,
            Options.Create(new BackupOptions { Directory = backupDir, PluginSettingsDirectory = pluginDir }),
            new TestEnvironment(),
            NullLogger<BackupService>.Instance);

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "domovoy-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Domovoy.IntegrationTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
