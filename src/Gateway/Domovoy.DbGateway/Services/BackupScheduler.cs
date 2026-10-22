// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Daily scheduled backups (Epic 3A). Ticks once a minute; when the configured local time has passed and
/// no run covered today's slot yet, it creates a bundle and applies retention. "Local" means the site
/// timezone (Epic 2K) when one is set — the same clock the user schedules everything else by. A failed
/// run still stamps <c>LastRunAt</c> so it is reported once, not retried every minute until midnight.
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IMongoDatabase _db;
    private readonly BackupService _backup;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(IMongoDatabase db, BackupService backup, ILogger<BackupScheduler> logger)
    {
        _db = db;
        _backup = backup;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the stack settle (Mongo healthy, EventInterceptor init) before a possible first-boot run.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backup scheduler tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var settings = await BackupSettingsStore.GetOrDefaultAsync(_db, ct);
        if (!settings.Enabled) return;
        if (!TimeOnly.TryParseExact(settings.Time, "HH:mm", out var time)) return;

        var tz = await ResolveTimeZoneAsync(ct);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var scheduledLocal = nowLocal.Date.Add(time.ToTimeSpan());
        if (nowLocal < scheduledLocal) return;

        var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(scheduledLocal, DateTimeKind.Unspecified), tz);
        if (settings.LastRunAt is { } last && last >= scheduledUtc) return;

        try
        {
            var result = await _backup.CreateBackupAsync("scheduled", ct);
            await BackupSettingsStore.RecordRunAsync(_db, ok: true, file: result.FileName, error: null, ct);
            _backup.ApplyRetention(settings.KeepCount);
        }
        catch (BackupBusyException)
        {
            // A manual run is in flight; its own stamp will cover today's slot.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled backup failed");
            await BackupSettingsStore.RecordRunAsync(_db, ok: false, file: null, error: ex.Message, ct);
        }
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken ct)
    {
        try
        {
            var location = await _db.GetCollection<SiteLocation>(SettingsEndpoints.Collection)
                .Find(x => x.Id == SiteLocation.SingletonId)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(location?.TimeZoneId))
                return SiteTimeZone.Resolve(location.TimeZoneId);
        }
        catch (Exception ex)
        {
            // Раньше ловился только TimeZoneNotFoundException, а недоступный Mongo ронял планировщик.
            _logger.LogWarning(ex, "Site timezone not resolvable — falling back to the server timezone");
        }

        return TimeZoneInfo.Local;
    }
}
