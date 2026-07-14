// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml.Templates;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Orchestrates the ML substrate (roadmap Epic 2A; multi-target tasks Epic 2P): iterates the runtime-editable
/// ML tasks (<c>ml_tasks</c>), trains each enabled task on its own schedule from the telemetry feature store
/// (P0-5/1B), registers the models in the DbGateway registry (pruned to the task's retention), and keeps the
/// inference <see cref="MlModelService"/> loaded. Every attempt's outcome is written back to the task's status
/// — the "why is it (not) training" signal for the UI. Training also runs on demand via
/// <c>POST /api/ml/train</c>. On startup it seeds the options-derived default task (first run only).
/// Registered as a hosted service + injectable for the endpoints.
/// </summary>
public sealed class MlTrainingService : BackgroundService
{
    private readonly DbGatewayClient _db;
    private readonly ModelTemplateRegistry _templates;
    private readonly MlModelService _models;
    private readonly ZoneCache _zones;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlTrainingService> _logger;

    // The gateway serves at most 5000 rows per request, newest-first — training pages through the
    // window instead of silently truncating dense telemetry (e.g. per-minute sensors over 30 days).
    private const int PageSize = 5000;
    private const int MaxTrainSamples = 100_000;

    public MlTrainingService(
        DbGatewayClient db, ModelTemplateRegistry templates, MlModelService models, ZoneCache zones,
        IOptions<AutomationOptions> options, ILogger<MlTrainingService> logger)
    {
        _db = db;
        _templates = templates;
        _models = models;
        _zones = zones;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Outcome of a training attempt for the API.</summary>
    public sealed record TrainResult(bool Trained, string Message, MlModel? Model);

    /// <summary>Per-task outcome of a "train all" run (Epic 2P).</summary>
    public sealed record TaskTrainResult(string TaskId, string Target, TrainResult Result);

    /// <summary>One backtest point for the scorecard chart (roadmap Epic 2B): model prediction vs reality.</summary>
    public sealed record BacktestPoint(DateTime Timestamp, double Predicted, double Actual);

    /// <summary>
    /// The scorecard payload: the serving model's metadata + a predicted-vs-actual series. For enum targets
    /// the series has no numeric points; <see cref="HitRate"/> (share of matching class predictions) is the
    /// signal instead.
    /// </summary>
    public sealed record Backtest(MlModel? Model, IReadOnlyList<BacktestPoint> Points, double? HitRate = null);

    /// <summary>Sample availability of one scope for the data-sufficiency check (Epic 2P).</summary>
    public sealed record ScopeDataCheck(string Level, string Key, long Samples, int Required, bool Sufficient);

    /// <summary>
    /// "Will this train?" diagnostics for a (prospective) task (Epic 2P): raw sample counts per scope over the
    /// window — an upper-bound estimate (label encoding may drop unparseable events) — plus whether any model
    /// template exists for the target's value-type at all.
    /// </summary>
    public sealed record DataCheck(
        string Target, int WindowDays, int MinSamples, string Kind, bool TemplateAvailable,
        IReadOnlyList<ScopeDataCheck> Scopes);

    // ===== Scheduling =====

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First-run seed of the options-derived default task (idempotent on the gateway; Epic 2P).
        await _db.SeedDefaultMlTaskAsync(BuildDefaultTask(_options), stoppingToken);

        await _models.RefreshAsync(stoppingToken); // pick up any pre-existing models

        var refresh = TimeSpan.FromMinutes(Math.Max(1, _options.ModelRefreshMinutes));
        using var timer = new PeriodicTimer(refresh);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TrainDueTasksAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Scheduled training cycle failed"); }

            await _models.RefreshAsync(stoppingToken);
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    // Train every enabled task whose interval has elapsed since its last ATTEMPT (persisted in the task's
    // status, so the schedule survives restarts). One task's failure never blocks the others.
    private async Task TrainDueTasksAsync(CancellationToken ct)
    {
        var tasks = await _db.GetMlTasksAsync(ct);
        if (tasks is null) return;

        var now = DateTime.UtcNow;
        foreach (var task in tasks.Where(t => t.Enabled))
        {
            var last = task.Status?.LastTrainAt ?? DateTime.MinValue;
            if ((now - last).TotalHours < task.TrainIntervalHours) continue;

            try { await TrainTaskAsync(task, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Training task {Target} failed", task.TargetCapability); }
        }
    }

    /// <summary>Train every enabled task now (manual "train all"); per-task outcomes, never throws per task.</summary>
    public async Task<IReadOnlyList<TaskTrainResult>> TrainAllAsync(CancellationToken ct)
    {
        var tasks = await _db.GetMlTasksAsync(ct);
        if (tasks is null) return Array.Empty<TaskTrainResult>();

        var results = new List<TaskTrainResult>();
        foreach (var task in tasks.Where(t => t.Enabled))
        {
            TrainResult result;
            try { result = await TrainTaskAsync(task, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Training task {Target} failed", task.TargetCapability);
                result = new TrainResult(false, "training failed — see service logs", null);
            }
            results.Add(new TaskTrainResult(task.Id, task.TargetCapability, result));
        }
        return results;
    }

    /// <summary>Find a task by id, for the per-task manual train endpoint; null if unknown/unreachable.</summary>
    public async Task<MlTask?> FindTaskAsync(string taskId, CancellationToken ct)
    {
        var tasks = await _db.GetMlTasksAsync(ct);
        return tasks?.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
    }

    // ===== Training =====

    /// <summary>
    /// Train one task on recent history and register models, then reload them for inference. Always fits the
    /// global model; when the task's <see cref="MlTask.TrainZoneModels"/> is on, also fits shared per-zone-kind
    /// models and, where a zone's own data beats its fallback by the promotion margin, per-zone models
    /// (roadmap Epic 2I). The outcome (success or the reason it didn't train) is written to the task's status.
    /// The returned <see cref="TrainResult.Model"/> is the global model; zone scopes are best-effort.
    /// </summary>
    public async Task<TrainResult> TrainTaskAsync(MlTask task, CancellationToken ct)
    {
        var attemptAt = DateTime.UtcNow;
        var (result, samples, scopes) = await TrainTaskCoreAsync(task, ct);

        await _db.UpdateMlTaskStatusAsync(task.Id, new MlTaskStatus
        {
            LastTrainAt = attemptAt,
            LastTrainOk = result.Trained,
            LastMessage = result.Message,
            LastSampleCount = samples,
            LastRegisteredScopes = scopes,
        }, ct);

        return result;
    }

    private async Task<(TrainResult Result, int Samples, int Scopes)> TrainTaskCoreAsync(MlTask task, CancellationToken ct)
    {
        var from = DateTime.UtcNow.AddDays(-task.WindowDays);

        var targetKind = CapabilityKindResolver.KindOf(task.TargetCapability);
        var candidates = _templates.ForTarget(targetKind);
        if (candidates.Count == 0)
            return (new TrainResult(false, $"no template for {task.TargetCapability}", null), 0, 0);

        // Context-join (Epic 2B): the home-mode timeline over the window, attached to numeric training rows so a
        // context template can learn mode-dependent schedules. Best-effort — absent ⇒ mode features are "none".
        var modeTimeline = await _db.GetModeTimelineAsync(from, PageSize, ct)
            ?? (IReadOnlyList<(DateTime At, string Mode)>)Array.Empty<(DateTime At, string Mode)>();

        // 1) Global model — always trained; its absence fails the whole run.
        var global = await LoadSeriesAsync(task, targetKind, from, null, modeTimeline, ct);
        if (global is null)
            return (new TrainResult(false, "training data unavailable", null), 0, 0);
        if (global.Count < task.MinSamples)
            return (new TrainResult(false, $"not enough data ({global.Count}/{task.MinSamples})", null), global.Count, 0);

        var selected = SelectBest(candidates, global, task.MinSamples);
        if (selected is null)
            return (new TrainResult(false, "training produced no model", null), global.Count, 0);

        var registeredGlobal = await RegisterAsync(task, selected.Value, ModelScope.Global, ct);
        if (registeredGlobal is null)
            return (new TrainResult(false, "could not register model", null), global.Count, 0);

        // 2) Per-zone scopes (Epic 2I) — best-effort.
        var zoneModels = task.TrainZoneModels
            ? await TrainZoneScopesAsync(task, candidates, targetKind, from, modeTimeline, selected.Value.Result.HoldoutScore, ct)
            : 0;

        await _models.RefreshAsync(ct);
        _logger.LogInformation(
            "Trained {Name} on {Count} samples ({Metric} {Score:0.###}); +{Zones} zone model(s)",
            registeredGlobal.Name, selected.Value.Result.SampleCount, registeredGlobal.Metric,
            registeredGlobal.HoldoutScore, zoneModels);
        var message = zoneModels > 0 ? $"ok (+{zoneModels} zone models)" : "ok";
        return (new TrainResult(true, message, registeredGlobal), global.Count, 1 + zoneModels);
    }

    // Fit shared zone-kind models (the fallbacks) and per-zone models that beat their fallback by the margin.
    private async Task<int> TrainZoneScopesAsync(
        MlTask task, IReadOnlyList<IModelTemplate> candidates, CapabilityKind targetKind, DateTime from,
        IReadOnlyList<(DateTime At, string Mode)> modeTimeline, double globalScore, CancellationToken ct)
    {
        var registered = 0;
        var kindFallbackScore = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Shared per-zone-kind models (always registered when they train — they are the fallbacks).
        foreach (var kind in _zones.Kinds())
        {
            var samples = await CollectZoneSeries(task, _zones.ZonesOfKind(kind), targetKind, from, modeTimeline, ct);
            if (samples.Count < task.MinSamples) continue;

            var sel = SelectBest(candidates, samples, task.MinSamples);
            if (sel is null) continue;

            if (await RegisterAsync(task, sel.Value, ModelScope.ZoneKind(kind), ct) is not null)
            {
                kindFallbackScore[kind] = sel.Value.Result.HoldoutScore;
                registered++;
            }
        }

        // Per-zone models — promoted only when they beat their fallback (zone_kind, else global).
        foreach (var zoneId in _zones.AllZoneIds())
        {
            var samples = await CollectZoneSeries(task, new[] { zoneId }, targetKind, from, modeTimeline, ct);
            if (samples.Count < task.MinSamples) continue;

            var sel = SelectBest(candidates, samples, task.MinSamples);
            if (sel is null) continue;

            var kind = _zones.KindOf(zoneId);
            var fallback = kind is not null && kindFallbackScore.TryGetValue(kind, out var f) ? f : globalScore;
            if (!ModelTemplateRegistry.ShouldPromote(sel.Value.Template, sel.Value.Result.HoldoutScore, fallback, task.ZonePromotionMargin))
                continue;

            if (await RegisterAsync(task, sel.Value, ModelScope.Zone(zoneId), ct) is not null)
                registered++;
        }

        return registered;
    }

    // Best applicable template by honest holdout (model selection, Epic 2I), or null if none trains.
    private static (IModelTemplate Template, TemplateResult Result)? SelectBest(
        IReadOnlyList<IModelTemplate> candidates, IReadOnlyList<LabeledSample> labeled, int minSamples)
    {
        (IModelTemplate Template, TemplateResult Result)? best = null;
        foreach (var template in candidates)
        {
            var r = template.Train(labeled, minSamples);
            if (r is null) continue;
            if (best is null || ModelTemplateRegistry.IsBetter(template, r.HoldoutScore, best.Value.Result.HoldoutScore))
                best = (template, r);
        }
        return best;
    }

    private async Task<MlModel?> RegisterAsync(
        MlTask task, (IModelTemplate Template, TemplateResult Result) selected, ModelScope scope, CancellationToken ct)
    {
        var (template, r) = selected;
        var model = new MlModel
        {
            Name = $"{task.TargetCapability} {template.Kind} [{scope}]",
            Kind = template.Kind,
            TargetCapability = task.TargetCapability,
            Scope = scope,
            SampleCount = r.SampleCount,
            Rmse = r.InSampleError,
            HoldoutMae = template.Metric == "MAE" ? r.HoldoutScore : 0, // back-compat (regression only)
            HoldoutScore = r.HoldoutScore,
            HoldoutSampleCount = r.HoldoutCount,
            Metric = template.Metric,
            Features = template.Features,
            Algorithm = template.Algorithm,
        };
        return await _db.RegisterModelAsync(model, r.Artifact, task.KeepLastVersions, ct);
    }

    private async Task<List<LabeledSample>> CollectZoneSeries(
        MlTask task, IReadOnlyList<string> zoneIds, CapabilityKind targetKind, DateTime from,
        IReadOnlyList<(DateTime At, string Mode)> modeTimeline, CancellationToken ct)
    {
        var all = new List<LabeledSample>();
        foreach (var zoneId in zoneIds)
        {
            var s = await LoadSeriesAsync(task, targetKind, from, zoneId, modeTimeline, ct);
            if (s is not null) all.AddRange(s);
        }
        return all;
    }

    /// <summary>
    /// Load the labeled training series for the task's target (roadmap Epic 2I, Phase 1): numeric targets read
    /// telemetry (sensor_readings); boolean targets read state-change events and encode them 0/1. Optionally
    /// zone-scoped. Null only when the source is unreachable.
    /// </summary>
    private async Task<List<LabeledSample>?> LoadSeriesAsync(
        MlTask task, CapabilityKind targetKind, DateTime from, string? zoneId,
        IReadOnlyList<(DateTime At, string Mode)> modeTimeline, CancellationToken ct)
    {
        if (targetKind == CapabilityKind.Number)
        {
            var telemetry = await LoadPagedAsync(
                (hi, token) => _db.GetTelemetryAsync(task.TargetCapability, from, PageSize, token, zoneId, hi),
                s => s.Timestamp, ct);
            if (telemetry is null) return null;
            // Attach the home mode in effect at each sample (Epic 2B context-join) for the context template.
            var rows = telemetry.Select(s => new LabeledSample(s.Timestamp, s.Value)).ToList();
            return ContextFeatureJoin.WithMode(rows, modeTimeline);
        }

        // Boolean/enum targets are labeled from the event-log: booleans encode to 0/1, enums keep the class.
        var events = await LoadPagedAsync(
            (hi, token) => _db.GetCapabilityEventsAsync(task.TargetCapability, from, PageSize, token, zoneId, hi),
            e => e.Timestamp, ct);
        if (events is null) return null;

        if (targetKind == CapabilityKind.Enum)
            return events
                .Select(e => (e.Timestamp, Class: EventLabelEncoder.ToClass(e.NewValue)))
                .Where(x => x.Class is not null)
                .Select(x => new LabeledSample(x.Timestamp, 0, x.Class))
                .ToList();

        return events
            .Select(e => (e.Timestamp, Label: EventLabelEncoder.ToLabel(e.NewValue)))
            .Where(x => x.Label is not null)
            .Select(x => new LabeledSample(x.Timestamp, x.Label!.Value))
            .ToList();
    }

    /// <summary>
    /// Pages a newest-first gateway endpoint by walking the upper time bound down until a short page.
    /// Returns oldest-first. Null only when the very first page fails (source unreachable); a failure
    /// mid-pagination keeps the newest pages already fetched — same data the pre-paging code trained on.
    /// </summary>
    private async Task<List<T>?> LoadPagedAsync<T>(
        Func<DateTime?, CancellationToken, Task<List<T>?>> fetch, Func<T, DateTime> timestamp, CancellationToken ct)
    {
        var all = new List<T>();
        DateTime? hi = null;
        while (all.Count < MaxTrainSamples)
        {
            var page = await fetch(hi, ct);
            if (page is null)
            {
                if (all.Count == 0) return null;
                _logger.LogWarning("Training fetch failed mid-pagination; continuing with {Count} samples", all.Count);
                break;
            }

            all.AddRange(page);
            if (page.Count < PageSize) break;

            // Step past the oldest sample of the page; sub-millisecond ties at the boundary are lost,
            // which is negligible for training data.
            hi = page.Min(timestamp).AddMilliseconds(-1);
        }

        if (all.Count >= MaxTrainSamples)
            _logger.LogWarning("Training fetch capped at {Max} samples; oldest data in the window was skipped", MaxTrainSamples);

        all.Sort((a, b) => timestamp(a).CompareTo(timestamp(b)));
        return all;
    }

    // ===== Diagnostics (Epic 2P) =====

    /// <summary>
    /// "Will this train?" — raw sample counts over the window, per scope (global + zone kinds + zones when
    /// <paramref name="zones"/> is set), from Mongo counters (no data paging). Serves both the task wizard
    /// (before a task exists) and the task card, which passes the task's own parameters.
    /// </summary>
    public async Task<DataCheck> CheckDataAsync(string target, int windowDays, int minSamples, bool zones, CancellationToken ct)
    {
        var from = DateTime.UtcNow.AddDays(-Math.Max(1, windowDays));
        var kind = CapabilityKindResolver.KindOf(target);
        var numeric = kind == CapabilityKind.Number;
        var templateAvailable = _templates.ForTarget(kind).Count > 0;

        var total = numeric
            ? await _db.CountTelemetryAsync(target, from, ct)
            : await _db.CountCapabilityEventsAsync(target, from, ct);

        var scopes = new List<ScopeDataCheck>
        {
            new(ModelScope.Global.Level, "", total ?? 0, minSamples, (total ?? 0) >= minSamples),
        };

        if (zones)
        {
            var byZone = numeric
                ? await _db.CountTelemetryByZoneAsync(target, from, ct)
                : await _db.CountCapabilityEventsByZoneAsync(target, from, ct);
            if (byZone is not null)
            {
                foreach (var zoneKind in _zones.Kinds())
                {
                    var count = _zones.ZonesOfKind(zoneKind).Sum(z => byZone.TryGetValue(z, out var c) ? c : 0);
                    scopes.Add(new ScopeDataCheck("zone_kind", zoneKind, count, minSamples, count >= minSamples));
                }
                foreach (var zoneId in _zones.AllZoneIds())
                {
                    var count = byZone.TryGetValue(zoneId, out var c) ? c : 0;
                    scopes.Add(new ScopeDataCheck("zone", zoneId, count, minSamples, count >= minSamples));
                }
            }
        }

        return new DataCheck(target, windowDays, minSamples, kind.ToString(), templateAvailable, scopes);
    }

    // ===== Backtest (Epic 2B / 2P) =====

    /// <summary>Backtest the options-default target's global model (back-compat overload).</summary>
    public Task<Backtest> BacktestAsync(int days, CancellationToken ct) =>
        BacktestAsync(_options.TrainCapability, null, null, days, ct);

    /// <summary>
    /// Score the serving model of (target, scope) against recent history (roadmap Epic 2B/2P) — "prediction vs
    /// fact". Numeric targets compare predicted vs measured values; boolean targets compare the predicted
    /// probability vs the 0/1 state; enum targets report the class <see cref="Backtest.HitRate"/> (no numeric
    /// series). Empty when no model is loaded for the scope or history is unavailable.
    /// </summary>
    public async Task<Backtest> BacktestAsync(string? target, string? level, string? key, int days, CancellationToken ct)
    {
        target = string.IsNullOrWhiteSpace(target) ? _options.TrainCapability : target;
        var scope = ResolveScope(level, key);
        var model = _models.LatestFor(target, scope);
        if (model is null) return new Backtest(null, Array.Empty<BacktestPoint>());

        var from = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        var targetKind = CapabilityKindResolver.KindOf(target);
        var chain = new[] { scope }; // strictly this scope's model — the scorecard scores what it serves

        if (targetKind == CapabilityKind.Enum)
        {
            var events = await LoadScopedEventsAsync(target, from, scope, ct);
            int hits = 0, judged = 0;
            foreach (var e in events)
            {
                var actual = EventLabelEncoder.ToClass(e.NewValue);
                if (actual is null) continue;
                var predicted = _models.TryPredictClass(target, new DateTimeOffset(e.Timestamp, TimeSpan.Zero), chain, 0);
                if (predicted is null) continue;
                judged++;
                if (string.Equals(predicted, actual, StringComparison.OrdinalIgnoreCase)) hits++;
            }
            return new Backtest(model, Array.Empty<BacktestPoint>(), judged == 0 ? null : (double)hits / judged);
        }

        List<(DateTime Timestamp, double Actual)> series;
        if (targetKind == CapabilityKind.Number)
        {
            var samples = await LoadScopedTelemetryAsync(target, from, scope, ct);
            series = samples.Select(s => (s.Timestamp, s.Value)).ToList();
        }
        else
        {
            var events = await LoadScopedEventsAsync(target, from, scope, ct);
            series = events
                .Select(e => (e.Timestamp, Label: EventLabelEncoder.ToLabel(e.NewValue)))
                .Where(x => x.Label is not null)
                .Select(x => (x.Timestamp, x.Label!.Value))
                .ToList();
        }

        var points = new List<BacktestPoint>(series.Count);
        foreach (var (timestamp, actual) in series.OrderBy(s => s.Timestamp))
        {
            if (_models.TryPredict(target, new DateTimeOffset(timestamp, TimeSpan.Zero), chain, 0, out var predicted))
                points.Add(new BacktestPoint(timestamp, Math.Round((double)predicted, 2), Math.Round(actual, 2)));
        }
        return new Backtest(model, points);
    }

    private static ModelScope ResolveScope(string? level, string? key) => level switch
    {
        "zone" when !string.IsNullOrEmpty(key) => ModelScope.Zone(key),
        "zone_kind" when !string.IsNullOrEmpty(key) => ModelScope.ZoneKind(key),
        _ => ModelScope.Global,
    };

    // History for a scope: a zone reads its own series, a zone-kind unions its member zones, global reads all.
    private async Task<List<DbGatewayClient.TelemetrySample>> LoadScopedTelemetryAsync(
        string target, DateTime from, ModelScope scope, CancellationToken ct)
    {
        var all = new List<DbGatewayClient.TelemetrySample>();
        foreach (var zoneId in ScopeZoneFilters(scope))
        {
            var page = await _db.GetTelemetryAsync(target, from, PageSize, ct, zoneId);
            if (page is not null) all.AddRange(page);
        }
        return all;
    }

    private async Task<List<DbGatewayClient.EventLogEntry>> LoadScopedEventsAsync(
        string target, DateTime from, ModelScope scope, CancellationToken ct)
    {
        var all = new List<DbGatewayClient.EventLogEntry>();
        foreach (var zoneId in ScopeZoneFilters(scope))
        {
            var page = await _db.GetCapabilityEventsAsync(target, from, PageSize, ct, zoneId);
            if (page is not null) all.AddRange(page);
        }
        return all;
    }

    // The zone filters a scope's history query needs: [null] = unfiltered (global), a single zone, or every
    // zone of a kind.
    private IEnumerable<string?> ScopeZoneFilters(ModelScope scope) => scope.Level switch
    {
        "zone" => new[] { scope.Key },
        "zone_kind" => _zones.ZonesOfKind(scope.Key),
        _ => new string?[] { null },
    };

    /// <summary>The options-derived default task seeded on first run (Epic 2P back-compat).</summary>
    internal static MlTask BuildDefaultTask(AutomationOptions o) => new()
    {
        Id = "default",
        Name = o.TrainCapability,
        TargetCapability = o.TrainCapability,
        Enabled = true,
        WindowDays = o.TrainWindowDays,
        MinSamples = o.MinSamples,
        TrainIntervalHours = o.TrainIntervalHours,
        TrainZoneModels = o.TrainZoneModels,
        ZonePromotionMargin = o.ZonePromotionMargin,
        ClampMin = o.SetpointMin,
        ClampMax = o.SetpointMax,
        KeepLastVersions = 10,
    };
}
