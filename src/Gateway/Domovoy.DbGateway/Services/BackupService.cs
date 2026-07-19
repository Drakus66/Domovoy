// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

using Domovoy.DbGateway.Config;
using Domovoy.DbGateway.Models;

using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Creates and restores full backup bundles (roadmap Epic 3A). A bundle is a zip with
/// <c>manifest.json</c> + <c>collections/{name}.bson</c> (concatenated BSON documents, the mongodump
/// <c>.bson</c> framing) + optional <c>plugin-settings/*.json</c> and <c>extras/**</c>. Dump/restore go
/// through the MongoDB driver — the DbGateway is the only service talking to Mongo, and needing no
/// <c>mongodump</c> binary keeps the container unchanged. Restore is manifest-driven: listed collections
/// are dropped and re-created (time-series with their original options), so a restore on a clean stack
/// rebuilds the exact shape; secondary indexes/TTL are re-ensured by the post-restore service restart
/// (<see cref="TimeSeriesInitializer"/> runs on EventInterceptor startup).
/// </summary>
public sealed class BackupService
{
    public const string FilePrefix = "domovoy-backup-";

    /// <summary>Operational side-channels that must not be bundled: capped Serilog log + Mongo internals.</summary>
    private static readonly string[] ExcludedCollections = { "ops_logs" };

    private static readonly Regex FileNamePattern = new(@"^domovoy-backup-[A-Za-z0-9\-]+\.zip$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // Backup and restore are mutually exclusive (and non-reentrant): concurrent runs would race on
    // collections and on the retention sweep.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly IMongoDatabase _db;
    private readonly BackupOptions _options;
    private readonly string _directory;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IMongoDatabase db, IOptions<BackupOptions> options, IHostEnvironment env, ILogger<BackupService> logger)
    {
        _db = db;
        _options = options.Value;
        _directory = Path.IsPathRooted(_options.Directory)
            ? _options.Directory
            : Path.Combine(env.ContentRootPath, _options.Directory);
        _logger = logger;
    }

    public sealed record BackupRunResult(string FileName, long SizeBytes, BackupManifest Manifest);

    public sealed record RestoreResult(
        int Collections, long Documents, int PluginSettingsRestored, string? ExtrasStagingDirectory);

    public sealed record BackupListItem(
        string FileName, long SizeBytes, DateTime CreatedAt, string? Reason, int? Collections, long? Documents,
        bool Valid);

    /// <summary>Dump every user collection (+ plugin settings + extra dirs) into a new bundle.</summary>
    public async Task<BackupRunResult> CreateBackupAsync(string reason, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new BackupBusyException();
        try
        {
            Directory.CreateDirectory(_directory);

            var createdAt = DateTime.UtcNow;
            var fileName = $"{FilePrefix}{createdAt:yyyyMMdd-HHmmss}.zip";
            var finalPath = Path.Combine(_directory, fileName);
            var tmpPath = finalPath + ".tmp";

            var manifest = new BackupManifest
            {
                CreatedAt = createdAt,
                Database = _db.DatabaseNamespace.DatabaseName,
                Reason = reason,
                AppVersion = typeof(BackupService).Assembly.GetName().Version?.ToString(),
            };

            try
            {
                await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var (name, tsInfo) in await ListBackupableCollectionsAsync(ct))
                    {
                        var entry = zip.CreateEntry($"collections/{name}.bson", CompressionLevel.Fastest);
                        await using var es = entry.Open();

                        long count = 0;
                        var collection = _db.GetCollection<BsonDocument>(name);
                        using var cursor = await collection
                            .Find(FilterDefinition<BsonDocument>.Empty)
                            .ToCursorAsync(ct);
                        while (await cursor.MoveNextAsync(ct))
                        {
                            foreach (var doc in cursor.Current)
                            {
                                var bytes = doc.ToBson();
                                await es.WriteAsync(bytes, ct);
                                count++;
                            }
                        }

                        manifest.Collections.Add(new BackupCollectionInfo
                        {
                            Name = name,
                            Documents = count,
                            TimeSeries = tsInfo,
                        });
                    }

                    AddPluginSettings(zip, manifest);
                    AddExtras(zip, manifest);

                    var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using var ms = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(ms, manifest, ManifestJson, ct);
                }

                File.Move(tmpPath, finalPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
                throw;
            }

            var size = new FileInfo(finalPath).Length;
            _logger.LogInformation(
                "Backup {File} created ({Collections} collections, {Documents} documents, {Size} bytes, reason={Reason})",
                fileName, manifest.Collections.Count, manifest.Collections.Sum(c => c.Documents), size, reason);

            return new BackupRunResult(fileName, size, manifest);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Restore a bundle in place: drop + re-create each manifest collection and re-insert its documents.
    /// Plugin settings are written back to the mounted settings directory; extras only go to a staging
    /// folder. The caller is expected to restart the stack afterwards so every service reloads its state.
    /// </summary>
    public async Task<RestoreResult> RestoreBackupAsync(string fileName, CancellationToken ct = default)
    {
        var path = ResolveExistingFile(fileName);

        if (!await _gate.WaitAsync(0, ct)) throw new BackupBusyException();
        try
        {
            using var zip = OpenBundle(path);
            var manifest = ReadValidManifest(zip);

            long totalDocs = 0;
            foreach (var info in manifest.Collections)
            {
                if (string.IsNullOrWhiteSpace(info.Name) || info.Name.Contains('$') || info.Name.StartsWith("system."))
                    throw new BackupFormatException($"illegal collection name '{info.Name}' in manifest");

                await _db.DropCollectionAsync(info.Name, ct);
                await CreateCollectionAsync(info, ct);

                var entry = zip.GetEntry($"collections/{info.Name}.bson");
                if (entry is null) continue; // listed but absent — treat as empty

                totalDocs += await InsertFromEntryAsync(info.Name, entry, ct);
            }

            var settingsRestored = RestorePluginSettings(zip);
            var extrasStaging = RestoreExtrasToStaging(zip, fileName);

            _logger.LogWarning(
                "Backup {File} restored: {Collections} collections, {Documents} documents, {Plugins} plugin-settings files{Extras}",
                fileName, manifest.Collections.Count, totalDocs, settingsRestored,
                extrasStaging is null ? "" : $", extras staged at {extrasStaging}");

            return new RestoreResult(manifest.Collections.Count, totalDocs, settingsRestored, extrasStaging);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>List bundles on disk, newest first (the stamped file name sorts chronologically).</summary>
    public IReadOnlyList<BackupListItem> List()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<BackupListItem>();

        var items = new List<BackupListItem>();
        foreach (var path in Directory.GetFiles(_directory, FilePrefix + "*.zip").OrderByDescending(Path.GetFileName))
        {
            var file = new FileInfo(path);
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var manifest = ReadManifest(zip);
                items.Add(new BackupListItem(
                    file.Name, file.Length,
                    manifest?.CreatedAt ?? file.LastWriteTimeUtc,
                    manifest?.Reason,
                    manifest?.Collections.Count,
                    manifest?.Collections.Sum(c => c.Documents),
                    Valid: manifest is not null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backup {File} is unreadable", file.Name);
                items.Add(new BackupListItem(file.Name, file.Length, file.LastWriteTimeUtc, null, null, null, false));
            }
        }

        return items;
    }

    public void Delete(string fileName) => File.Delete(ResolveExistingFile(fileName));

    /// <summary>Absolute path of an existing bundle — for the download endpoint.</summary>
    public string ResolveExistingFile(string fileName)
    {
        if (fileName != Path.GetFileName(fileName) || !FileNamePattern.IsMatch(fileName))
            throw new BackupFormatException($"illegal backup file name '{fileName}'");
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path)) throw new BackupNotFoundException(fileName);
        return path;
    }

    /// <summary>
    /// Accept an uploaded bundle (host migration): persist it under a stamped name, then validate the
    /// manifest so a broken upload never sits in the list looking restorable.
    /// </summary>
    public async Task<string> SaveUploadAsync(Stream body, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        var fileName = $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}-upload.zip";
        var path = Path.Combine(_directory, fileName);

        try
        {
            await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                await body.CopyToAsync(fs, ct);

            using var zip = OpenBundle(path);
            ReadValidManifest(zip);
        }
        catch
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
            throw;
        }

        _logger.LogInformation("Backup bundle uploaded as {File}", fileName);
        return fileName;
    }

    /// <summary>Delete the oldest bundles beyond <paramref name="keepCount"/>.</summary>
    public int ApplyRetention(int keepCount)
    {
        if (keepCount < 1 || !Directory.Exists(_directory)) return 0;

        var victims = Directory.GetFiles(_directory, FilePrefix + "*.zip")
            .OrderByDescending(Path.GetFileName)
            .Skip(keepCount)
            .ToList();
        foreach (var path in victims)
        {
            try
            {
                File.Delete(path);
                _logger.LogInformation("Backup retention: deleted {File}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backup retention: could not delete {File}", Path.GetFileName(path));
            }
        }

        return victims.Count;
    }

    // ----- dump helpers -----

    private async Task<List<(string Name, BackupTimeSeriesInfo? TimeSeries)>> ListBackupableCollectionsAsync(
        CancellationToken ct)
    {
        var infos = await (await _db.ListCollectionsAsync(cancellationToken: ct)).ToListAsync(ct);

        var result = new List<(string, BackupTimeSeriesInfo?)>();
        foreach (var info in infos)
        {
            var name = info["name"].AsString;
            var type = info.TryGetValue("type", out var t) ? t.AsString : "collection";

            if (name.StartsWith("system.") || ExcludedCollections.Contains(name)) continue;
            // Views have no own data; the only view-like thing we keep is the time-series collection itself
            // (its hidden system.buckets storage is skipped above and rebuilt by CreateCollection on restore).
            if (type == "view") continue;

            BackupTimeSeriesInfo? ts = null;
            if (type == "timeseries" &&
                info.TryGetValue("options", out var options) &&
                options.AsBsonDocument.TryGetValue("timeseries", out var tsOptions))
            {
                var tsDoc = tsOptions.AsBsonDocument;
                ts = new BackupTimeSeriesInfo
                {
                    TimeField = tsDoc.TryGetValue("timeField", out var tf) ? tf.AsString : "Timestamp",
                    MetaField = tsDoc.TryGetValue("metaField", out var mf) ? mf.AsString : null,
                    Granularity = tsDoc.TryGetValue("granularity", out var g) ? g.AsString : null,
                };
            }

            result.Add((name, ts));
        }

        return result.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    private void AddPluginSettings(ZipArchive zip, BackupManifest manifest)
    {
        var dir = _options.PluginSettingsDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            zip.CreateEntryFromFile(path, $"plugin-settings/{name}", CompressionLevel.Optimal);
            manifest.PluginSettings.Add(name);
        }
    }

    private void AddExtras(ZipArchive zip, BackupManifest manifest)
    {
        foreach (var dir in _options.IncludeDirectories.Where(Directory.Exists))
        {
            var root = new DirectoryInfo(dir);
            var label = root.Name;
            while (manifest.Extras.Contains(label)) label += "-2";

            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
                zip.CreateEntryFromFile(file.FullName, $"extras/{label}/{rel}", CompressionLevel.Optimal);
            }

            manifest.Extras.Add(label);
        }
    }

    // ----- restore helpers -----

    private async Task CreateCollectionAsync(BackupCollectionInfo info, CancellationToken ct)
    {
        CreateCollectionOptions? options = null;
        if (info.TimeSeries is { } ts)
        {
            TimeSeriesGranularity? granularity = Enum.TryParse<TimeSeriesGranularity>(ts.Granularity, true, out var g)
                ? g : null;
            options = new CreateCollectionOptions
            {
                TimeSeriesOptions = new TimeSeriesOptions(ts.TimeField, ts.MetaField, granularity),
            };
        }

        try
        {
            await _db.CreateCollectionAsync(info.Name, options, ct);
        }
        catch (MongoCommandException ex) when (ex.Message.Contains("already exists"))
        {
            // A concurrent writer re-created it between drop and create; documents still insert fine.
            _logger.LogWarning("Collection {Collection} was re-created concurrently during restore", info.Name);
        }
    }

    private async Task<long> InsertFromEntryAsync(string collectionName, ZipArchiveEntry entry, CancellationToken ct)
    {
        var collection = _db.GetCollection<BsonDocument>(collectionName);

        long total = 0;
        var batch = new List<BsonDocument>();
        long batchBytes = 0;
        const long FlushBytes = 8 * 1024 * 1024;
        const int FlushCount = 500;

        await using var stream = entry.Open();
        while (true)
        {
            var doc = await ReadBsonDocumentAsync(stream, ct);
            if (doc is null) break;

            batch.Add(doc.Value.Document);
            batchBytes += doc.Value.Size;
            total++;

            if (batch.Count >= FlushCount || batchBytes >= FlushBytes)
            {
                await collection.InsertManyAsync(batch, cancellationToken: ct);
                batch.Clear();
                batchBytes = 0;
            }
        }

        if (batch.Count > 0)
            await collection.InsertManyAsync(batch, cancellationToken: ct);

        return total;
    }

    /// <summary>Read one length-prefixed BSON document from the mongodump-style stream; null at EOF.</summary>
    private static async Task<(BsonDocument Document, int Size)?> ReadBsonDocumentAsync(
        Stream stream, CancellationToken ct)
    {
        var lengthBytes = new byte[4];
        var read = await ReadUpToAsync(stream, lengthBytes, 4, ct);
        if (read == 0) return null;
        if (read < 4) throw new BackupFormatException("truncated BSON stream (length prefix)");

        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length < 5 || length > 32 * 1024 * 1024)
            throw new BackupFormatException($"corrupt BSON stream (document length {length})");

        var buffer = new byte[length];
        Array.Copy(lengthBytes, buffer, 4);
        if (await ReadUpToAsync(stream, buffer, length - 4, ct, offset: 4) != length - 4)
            throw new BackupFormatException("truncated BSON stream (document body)");

        return (BsonSerializer.Deserialize<BsonDocument>(buffer), length);
    }

    private static async Task<int> ReadUpToAsync(
        Stream stream, byte[] buffer, int count, CancellationToken ct, int offset = 0)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private int RestorePluginSettings(ZipArchive zip)
    {
        var dir = _options.PluginSettingsDirectory;
        var entries = zip.Entries.Where(e => e.FullName.StartsWith("plugin-settings/") && e.Name.Length > 0).ToList();
        if (entries.Count == 0) return 0;

        if (string.IsNullOrWhiteSpace(dir))
        {
            _logger.LogWarning(
                "Bundle contains {Count} plugin-settings files but Backup:PluginSettingsDirectory is not configured — skipped",
                entries.Count);
            return 0;
        }

        Directory.CreateDirectory(dir);
        foreach (var entry in entries)
        {
            // Flat by construction; Path.GetFileName also disarms any hostile path in a foreign bundle.
            entry.ExtractToFile(Path.Combine(dir, Path.GetFileName(entry.Name)), overwrite: true);
        }

        return entries.Count;
    }

    private string? RestoreExtrasToStaging(ZipArchive zip, string bundleFileName)
    {
        var entries = zip.Entries.Where(e => e.FullName.StartsWith("extras/") && e.Name.Length > 0).ToList();
        if (entries.Count == 0) return null;

        var staging = Path.Combine(
            _directory, "restored-extras", Path.GetFileNameWithoutExtension(bundleFileName));
        var stagingFull = Path.GetFullPath(staging);

        foreach (var entry in entries)
        {
            var target = Path.GetFullPath(Path.Combine(staging, entry.FullName["extras/".Length..]));
            if (!target.StartsWith(stagingFull, StringComparison.Ordinal))
                throw new BackupFormatException($"illegal path '{entry.FullName}' in bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        return staging;
    }

    /// <summary>Open a bundle, translating "not even a zip" into the 400-shaped format error.</summary>
    private static ZipArchive OpenBundle(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new BackupFormatException($"not a zip archive: {ex.Message}");
        }
    }

    private static BackupManifest ReadValidManifest(ZipArchive zip)
    {
        var manifest = ReadManifest(zip)
            ?? throw new BackupFormatException("manifest.json is missing — not a Domovoy backup bundle");
        if (manifest.FormatVersion != BackupManifest.CurrentFormatVersion)
            throw new BackupFormatException($"unsupported bundle format version {manifest.FormatVersion}");
        return manifest;
    }

    private static BackupManifest? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json");
        if (entry is null) return null;
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, ManifestJson);
        }
        catch (JsonException ex)
        {
            throw new BackupFormatException($"manifest.json is malformed: {ex.Message}");
        }
    }
}

/// <summary>A backup or restore is already running — surfaces as HTTP 409.</summary>
public sealed class BackupBusyException : Exception
{
    public BackupBusyException() : base("a backup or restore is already in progress") { }
}

/// <summary>The requested bundle does not exist — surfaces as HTTP 404.</summary>
public sealed class BackupNotFoundException : Exception
{
    public BackupNotFoundException(string fileName) : base($"backup '{fileName}' not found") { }
}

/// <summary>The bundle (or a file name) is malformed — surfaces as HTTP 400.</summary>
public sealed class BackupFormatException : Exception
{
    public BackupFormatException(string message) : base(message) { }
}
