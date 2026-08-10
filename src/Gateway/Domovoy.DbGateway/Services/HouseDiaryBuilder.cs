// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.Contracts.Narrative;
using Domovoy.DbGateway.Endpoints;
using Domovoy.Narrative;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Background builder that materializes the House Diary (roadmap Epic 2N, Phase 2). Once per cycle it mines
/// each not-yet-built recent day's events into beats (<see cref="DiaryMiner"/>), coalesces them into causal
/// scenes (<see cref="SceneBuilder"/>), scores significance (<see cref="SignificanceScorer"/>), consolidates
/// the day (<see cref="DayConsolidator"/> — silent days are skipped), renders the prose through the resolved
/// deterministic renderer and upserts a <see cref="HomeStoryEntry"/>. Significance is computed once and
/// stored. Days are built in date order so the deterministic synonym rotation (<see cref="NarrativeState"/>)
/// advances correctly and never resets. A tier sweep prunes low-significance days older than a week — the
/// «&lt;7 дней всё, глубже только значимое» decay (reuses the Epic 1B retention idea on a plain collection,
/// since a time-series collection could not be upserted for idempotent rebuilds).
/// </summary>
public sealed class HouseDiaryBuilder : BackgroundService
{
    private const string Locale = "ru";
    private const string StateCollection = "narrative_state";
    private static readonly TimeSpan BuildInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan GraceDelay = TimeSpan.FromSeconds(30);
    private const int LookbackDays = 7;
    private const int BaselineDays = 30;
    private const int DeepTierDays = 7;
    private const double DeepScoreThreshold = 6.0;

    private readonly IMongoDatabase _db;
    private readonly DiaryMiner _miner;
    private readonly LanguagePackProvider _packs;
    private readonly INarrativeRendererSelector _selector;
    private readonly ILogger<HouseDiaryBuilder> _logger;

    public HouseDiaryBuilder(
        IMongoDatabase db, DiaryMiner miner, LanguagePackProvider packs,
        INarrativeRendererSelector selector, ILogger<HouseDiaryBuilder> logger)
    {
        _db = db;
        _miner = miner;
        _packs = packs;
        _selector = selector;
        _logger = logger;
    }

    private IMongoCollection<HomeStoryEntry> Stories => _db.GetCollection<HomeStoryEntry>(HomeStoryEndpoints.Collection);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(GraceDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        await EnsureIndexAsync(stoppingToken);
        _logger.LogInformation("House Diary builder started (build every {H}h, lookback {D}d)", BuildInterval.TotalHours, LookbackDays);

        using var timer = new PeriodicTimer(BuildInterval);
        do
        {
            try
            {
                await BuildPendingAsync(stoppingToken);
                await SweepDeepHistoryAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "House Diary build cycle failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    /// <summary>Build every recent day that has no diary entry yet (in date order so rotation advances).</summary>
    private async Task BuildPendingAsync(CancellationToken ct)
    {
        var tz = await ResolveTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz));

        var counts = await BaselinePatternCountsAsync(today, tz, ct);
        var state = await LoadStateAsync(ct);
        var changed = false;

        for (var offset = LookbackDays; offset >= 1; offset--)
        {
            var day = today.AddDays(-offset);
            var id = IdFor(day);
            var exists = await Stories.Find(x => x.Id == id).AnyAsync(ct);
            if (exists) continue;

            if (await BuildDayEntryAsync(day, tz, counts, state, ct) is not null)
                changed = true;
        }

        if (changed) await SaveStateAsync(state, ct);
    }

    /// <summary>Rebuild a single day on demand (manual endpoint) — deletes any existing entry first.</summary>
    public async Task<HomeStoryEntry?> RebuildDayAsync(DateOnly day, CancellationToken ct = default)
    {
        var tz = await ResolveTimeZoneAsync(ct);
        var counts = await BaselinePatternCountsAsync(day.AddDays(1), tz, ct);
        var state = await LoadStateAsync(ct);

        await Stories.DeleteOneAsync(x => x.Id == IdFor(day), ct);
        var built = await BuildDayEntryAsync(day, tz, counts, state, ct);
        await SaveStateAsync(state, ct);
        return built;
    }

    private async Task<HomeStoryEntry?> BuildDayEntryAsync(
        DateOnly day, TimeZoneInfo tz, IReadOnlyDictionary<string, int> counts, NarrativeState state, CancellationToken ct)
    {
        var (fromUtc, toUtc) = DayBoundsUtc(day, tz);
        var beats = await _miner.MineAsync(fromUtc, toUtc, ct);
        if (beats.Count == 0) return null;

        var scenes = SceneBuilder.Build(beats);
        var sctx = new SignificanceContext { PatternCounts = counts, TimeZone = tz };
        foreach (var s in scenes) SignificanceScorer.ScoreInto(s, sctx);

        var day0 = DayConsolidator.Consolidate(day, tz.Id, scenes);
        if (day0 is null) return null; // silence — nothing narrated

        var pack = await _packs.GetAsync(Locale, ct);
        var renderer = _selector.ResolveFor(Locale);
        var rendered = renderer.Render(day0, pack, state);
        if (string.IsNullOrWhiteSpace(rendered.Paragraph)) return null;

        var entry = new HomeStoryEntry
        {
            Id = IdFor(day),
            Date = fromUtc,
            Locale = renderer.Locale,
            Paragraph = rendered.Paragraph,
            Scenes = day0.Scenes,
            DayScore = day0.DayScore,
            Tier = 0,
            RendererTier = rendered.RendererTier,
            PackVersion = pack.PackVersion,
            BuiltAt = DateTime.UtcNow,
            Timestamp = fromUtc,
        };

        await Stories.ReplaceOneAsync(x => x.Id == entry.Id, entry, new ReplaceOptions { IsUpsert = true }, ct);
        _logger.LogInformation("Diary: narrated {Date} ({Scenes} scene(s), score {Score:0.0})", day, day0.Scenes.Count, day0.DayScore);
        return entry;
    }

    /// <summary>Prune low-significance days older than a week — «&lt;7 дней всё, глубже только значимое».</summary>
    private async Task SweepDeepHistoryAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-DeepTierDays);
        var b = Builders<HomeStoryEntry>.Filter;
        var result = await Stories.DeleteManyAsync(
            b.And(b.Lt(x => x.Date, cutoff), b.Lt(x => x.DayScore, DeepScoreThreshold)), ct);
        if (result.DeletedCount > 0)
            _logger.LogInformation("Diary: pruned {N} low-significance day(s) older than {D}d", result.DeletedCount, DeepTierDays);
    }

    /// <summary>Baseline occurrence counts of each archetype.capability.Transition over the trailing window.</summary>
    private async Task<IReadOnlyDictionary<string, int>> BaselinePatternCountsAsync(DateOnly upto, TimeZoneInfo tz, CancellationToken ct)
    {
        var (_, toUtc) = DayBoundsUtc(upto, tz);
        var fromUtc = toUtc.AddDays(-BaselineDays);
        var beats = await _miner.MineAsync(fromUtc, toUtc, ct);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var beat in beats)
        {
            var key = $"{beat.ArchetypeKey}.{beat.CapabilityId}.{beat.Transition}";
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        return counts;
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken ct)
    {
        try
        {
            var loc = await _db.GetCollection<SiteLocation>(SettingsEndpoints.Collection)
                .Find(x => x.Id == SiteLocation.SingletonId).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(loc?.TimeZoneId))
                return SiteTimeZone.Resolve(loc.TimeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve site time zone; using UTC");
        }
        return TimeZoneInfo.Utc;
    }

    private async Task<NarrativeState> LoadStateAsync(CancellationToken ct)
    {
        var doc = await _db.GetCollection<NarrativeState>(StateCollection)
            .Find(x => x.Id == "current").FirstOrDefaultAsync(ct);
        return doc ?? new NarrativeState();
    }

    private async Task SaveStateAsync(NarrativeState state, CancellationToken ct)
    {
        state.UpdatedAt = DateTime.UtcNow;
        await _db.GetCollection<NarrativeState>(StateCollection)
            .ReplaceOneAsync(x => x.Id == state.Id, state, new ReplaceOptions { IsUpsert = true }, ct);
    }

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        try
        {
            await Stories.Indexes.CreateOneAsync(
                new CreateIndexModel<HomeStoryEntry>(Builders<HomeStoryEntry>.IndexKeys.Descending(x => x.Date)),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure home_story index");
        }
    }

    private static (DateTime fromUtc, DateTime toUtc) DayBoundsUtc(DateOnly day, TimeZoneInfo tz)
    {
        var localStart = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), tz);
        return (fromUtc, toUtc);
    }

    private static string IdFor(DateOnly day) => $"story-{day:yyyy-MM-dd}";

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
