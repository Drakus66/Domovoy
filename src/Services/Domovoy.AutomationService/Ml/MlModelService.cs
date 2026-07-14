// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml.Templates;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Options;
using Microsoft.ML;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Loads and serves registered models for inference (roadmap Epic 2A; per-zone scoping Epic 2I; multi-target
/// tasks Epic 2P). It holds a cached scalar predictor per <b>(target, scope)</b> — every capability with
/// registered models is served, regardless of its task's enabled flag (disabling a task stops <i>training</i>,
/// not serving; removal from serving = deleting the model versions). The synchronous
/// <see cref="TryPredict(string, DateTimeOffset, IReadOnlyList{ModelScope}, int, out float)"/> is safe for the
/// (single-threaded) block tick and resolves a model along the instance's fallback chain. The predictor's
/// scalar is the model's natural output — a value for a regression (setpoint) model, a probability for a
/// binary (toggle) model — so the consuming governor interprets it for its value-type. Regression predictions
/// are soft-clamped to their task's <see cref="MlTask.ClampMin"/>/<see cref="MlTask.ClampMax"/> (the governor's
/// static safety floor stays the hard bound). Emits nothing until some model is loaded.
/// </summary>
public sealed class MlModelService
{
    private readonly DbGatewayClient _db;
    private readonly HomeModeState _mode;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlModelService> _logger;

    private readonly MLContext _ml = new(seed: 0);
    private readonly object _lock = new();

    // "{targetKey}|{scopeKey}" → loaded predictor (the latest version of that target's scope).
    private readonly Dictionary<string, Loaded> _byScope = new(StringComparer.Ordinal);

    // Version-pinned predictors (Epic 2C model_selection): "{targetKey}|{scopeKey}#v{version}" → loaded
    // predictor. Populated lazily — a tick asking for a version we haven't loaded registers it in
    // _pendingPins, the next background refresh fetches it, and until then the instance falls back to the
    // scope's latest model. Keeps the sync block tick allocation-free while the (async) artifact download
    // stays on the refresh thread.
    private readonly Dictionary<string, Loaded> _byVersion = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string TargetKey, string ScopeKey, int Version), byte> _pendingPins = new();

    // Task soft clamps per target (Epic 2P), applied to regression scalars; refreshed with the models.
    private Dictionary<string, (double? Min, double? Max)> _clampsByTarget = new(StringComparer.Ordinal);

    // A scope's loaded model exposes whichever predictor matches its kind: a scalar (regression value / binary
    // probability) for setpoint/toggle governors, or a class (enum label) for the selector governor.
    private sealed record Loaded(
        string ModelId, Func<DateTimeOffset, double>? Scalar, Func<DateTimeOffset, string?>? Class, MlModel Meta);

    public MlModelService(DbGatewayClient db, HomeModeState mode, IOptions<AutomationOptions> options, ILogger<MlModelService> logger)
    {
        _db = db;
        _mode = mode;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Metadata of the loaded global model of the options-default target (back-compat: status).</summary>
    public MlModel? Current { get; private set; }

    /// <summary>Metadata of the loaded model serving (target, scope), or null. For the scorecard (Epic 2P).</summary>
    public MlModel? LatestFor(string target, ModelScope scope)
    {
        lock (_lock)
            return _byScope.TryGetValue(Key(target, scope.AsKey()), out var loaded) ? loaded.Meta : null;
    }

    /// <summary>
    /// (Re)load the per-(target, scope) predictors from the registry — the latest version of every target+scope
    /// that has a model; scopes whose model disappeared are dropped (from the version-pin cache too, so a pruned
    /// pin doesn't keep serving from memory). Also refreshes the per-target task clamps (Epic 2P).
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var all = await _db.GetModelsAsync(ct);
        if (all is null) return;

        // Latest version per (target, scope), across all targets (Epic 2P).
        var latestByKey = all
            .GroupBy(m => Key(m.TargetCapability, (m.Scope ?? ModelScope.Global).AsKey()), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Version).First(), StringComparer.Ordinal);

        foreach (var (key, meta) in latestByKey)
        {
            lock (_lock)
            {
                if (_byScope.TryGetValue(key, out var loaded) && loaded.ModelId == meta.Id)
                    continue; // already current
            }

            var built = await LoadAsync(meta, ct);
            if (built is null) continue;

            lock (_lock) _byScope[key] = built;
            _logger.LogInformation(
                "Loaded ML model {Name} v{Version} scope {Scope} ({Metric} {Score:0.###})",
                meta.Name, meta.Version, meta.Scope, meta.Metric, meta.HoldoutScore);
        }

        var known = new HashSet<string>(all.Select(m => m.Id), StringComparer.Ordinal);
        lock (_lock)
        {
            // Drop (target, scope) cells whose model line is gone, and pinned versions deleted from the
            // registry (retention prune / manual delete) — a deleted pin falls back to the scope's latest.
            foreach (var stale in _byScope.Keys.Where(k => !latestByKey.ContainsKey(k)).ToList())
                _byScope.Remove(stale);
            foreach (var stale in _byVersion.Where(kv => !known.Contains(kv.Value.ModelId)).Select(kv => kv.Key).ToList())
                _byVersion.Remove(stale);

            Current = _byScope.TryGetValue(Key(_options.TrainCapability, ModelScope.Global.AsKey()), out var g)
                ? g.Meta
                : _byScope.Values.FirstOrDefault()?.Meta;
        }

        await LoadPendingPinsAsync(all, ct);
        await RefreshClampsAsync(ct);
    }

    // Per-target soft clamps from the ML tasks (Epic 2P). Best-effort — unreachable tasks keep the last set.
    private async Task RefreshClampsAsync(CancellationToken ct)
    {
        var tasks = await _db.GetMlTasksAsync(ct);
        if (tasks is null) return;

        var clamps = tasks
            .Where(t => t.ClampMin is not null || t.ClampMax is not null)
            .GroupBy(t => TargetKey(t.TargetCapability), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().ClampMin, g.First().ClampMax), StringComparer.Ordinal);
        lock (_lock) _clampsByTarget = clamps;
    }

    // Fetch any version pins a block tick has requested (Epic 2C). A pin for a version that doesn't exist in the
    // registry is dropped (the instance keeps using latest); a transient artifact-fetch failure is retried next
    // refresh. Loaded pins persist across refreshes (the set is tiny — one per pinned instance).
    private async Task LoadPendingPinsAsync(List<MlModel> all, CancellationToken ct)
    {
        foreach (var pin in _pendingPins.Keys.ToList())
        {
            var versionKey = VersionKey(pin.TargetKey, pin.ScopeKey, pin.Version);
            lock (_lock)
            {
                if (_byVersion.ContainsKey(versionKey)) { _pendingPins.TryRemove(pin, out _); continue; }
            }

            var meta = all.FirstOrDefault(m =>
                TargetKey(m.TargetCapability) == pin.TargetKey
                && (m.Scope ?? ModelScope.Global).AsKey() == pin.ScopeKey
                && m.Version == pin.Version);
            if (meta is null) { _pendingPins.TryRemove(pin, out _); continue; } // no such version — keep using latest

            var built = await LoadAsync(meta, ct);
            if (built is null) continue; // transient — retry next refresh (LoadAsync drops hard failures itself)

            lock (_lock) _byVersion[versionKey] = built;
            _pendingPins.TryRemove(pin, out _);
            _logger.LogInformation("Pinned ML model {Name} v{Version} scope {Scope} loaded (Epic 2C)",
                meta.Name, meta.Version, meta.Scope);
        }
    }

    // Download + build the predictor for one model; null on a transient fetch failure. Unsupported kinds and
    // corrupt artifacts are logged and reported as null too — callers simply don't serve them.
    private async Task<Loaded?> LoadAsync(MlModel meta, CancellationToken ct)
    {
        var bytes = await _db.GetModelArtifactAsync(meta.Id, ct);
        if (bytes is null) return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            var model = _ml.Model.Load(stream, out _);
            var (scalar, klass) = BuildPredictors(model, meta.Kind);
            if (scalar is null && klass is null)
            {
                _logger.LogWarning("No inference for model kind {Kind} ({Id})", meta.Kind, meta.Id);
                return null;
            }
            return new Loaded(meta.Id, scalar, klass, meta);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ML model {Id}", meta.Id);
            return null;
        }
    }

    private static string TargetKey(string target) => target.ToLowerInvariant();
    private static string Key(string target, string scopeKey) => $"{TargetKey(target)}|{scopeKey}";
    private static string VersionKey(string targetKey, string scopeKey, int version) => $"{targetKey}|{scopeKey}#v{version}";

    // Build the predictor matching a model's kind: regression → scalar value, binary → scalar probability,
    // multiclass → class label.
    private (Func<DateTimeOffset, double>? Scalar, Func<DateTimeOffset, string?>? Class) BuildPredictors(
        ITransformer model, string kind)
    {
        switch (kind)
        {
            case MlModelKinds.ScheduleRegression:
            {
                var engine = _ml.Model.CreatePredictionEngine<MlSample, MlPrediction>(model);
                return (now =>
                {
                    var (hour, dow) = Features(now);
                    return engine.Predict(new MlSample { Hour = hour, Dow = dow }).Value;
                }, null);
            }
            case MlModelKinds.ScheduleRegressionContext:
            {
                // Train/serve parity (Epic 2B): condition on the CURRENT home mode at inference — the same
                // ambient feature the context-join attached to each training row. Home mode is global, so the
                // predictor reads it here rather than threading it through the block tick.
                var engine = _ml.Model.CreatePredictionEngine<
                    Templates.ContextScheduleRegressionTemplate.ContextSample,
                    Templates.ContextScheduleRegressionTemplate.ContextPrediction>(model);
                return (now =>
                {
                    var (hour, dow) = Features(now);
                    var mode = _mode.Current ?? Templates.ContextScheduleRegressionTemplate.NoMode;
                    return engine.Predict(new Templates.ContextScheduleRegressionTemplate.ContextSample
                    {
                        Hour = hour, Dow = dow, Mode = mode,
                    }).Value;
                }, null);
            }
            case MlModelKinds.ScheduleBinary:
            {
                var engine = _ml.Model.CreatePredictionEngine<MlBinarySample, MlBinaryPrediction>(model);
                return (now =>
                {
                    var (hour, dow) = Features(now);
                    return engine.Predict(new MlBinarySample { Hour = hour, Dow = dow }).Probability;
                }, null);
            }
            case MlModelKinds.ScheduleMulticlass:
            {
                var engine = _ml.Model.CreatePredictionEngine<MlMulticlassSample, MlMulticlassPrediction>(model);
                return (null, now =>
                {
                    var (hour, dow) = Features(now);
                    var label = engine.Predict(new MlMulticlassSample { Hour = hour, Dow = dow }).PredictedLabel;
                    return string.IsNullOrEmpty(label) ? null : label;
                });
            }
            default:
                return (null, null);
        }
    }

    private static (float Hour, float Dow) Features(DateTimeOffset now) =>
        ((float)(now.Hour + now.Minute / 60.0), (float)(int)now.DayOfWeek);

    /// <summary>Predict for <paramref name="now"/> using the default target's global model (back-compat, Epic 2A blocks).</summary>
    public bool TryPredict(DateTimeOffset now, out float value) =>
        TryPredict(_options.TrainCapability, now, new[] { ModelScope.Global }, 0, out value);

    /// <summary>Predict the default target along <paramref name="chain"/> (back-compat overload).</summary>
    public bool TryPredict(DateTimeOffset now, IReadOnlyList<ModelScope> chain, out float value) =>
        TryPredict(_options.TrainCapability, now, chain, 0, out value);

    /// <summary>
    /// Predict <paramref name="target"/> for <paramref name="now"/> resolving the model along
    /// <paramref name="chain"/> (most specific first); false if no scope in the chain has a loaded model. The
    /// returned scalar is the model's natural output (regression value or binary probability); regression
    /// values are soft-clamped to the target task's clamps (Epic 2P). When <paramref name="pinnedVersion"/>
    /// &gt; 0 the serving scope's pinned version is used if loaded (Epic 2C model_selection); otherwise the pin
    /// is queued for the next refresh and the scope's latest model is used meanwhile.
    /// </summary>
    public bool TryPredict(string target, DateTimeOffset now, IReadOnlyList<ModelScope> chain, int pinnedVersion, out float value)
    {
        value = 0;
        var targetKey = TargetKey(target);
        lock (_lock)
        {
            foreach (var scope in chain)
            {
                var scopeKey = scope.AsKey();
                if (!_byScope.TryGetValue(Key(target, scopeKey), out var latest) || latest.Scalar is null) continue;
                var serving = ResolvePinned(targetKey, scopeKey, pinnedVersion, latest) ?? latest;
                value = (float)Clamp(targetKey, serving.Meta.Kind, serving.Scalar!(now));
                return true;
            }
        }
        return false;
    }

    /// <summary>Predict the default target's class label along <paramref name="chain"/> (back-compat overload).</summary>
    public string? TryPredictClass(DateTimeOffset now, IReadOnlyList<ModelScope> chain) =>
        TryPredictClass(_options.TrainCapability, now, chain, 0);

    /// <summary>
    /// Predict the class label of <paramref name="target"/> for <paramref name="now"/> along the scope
    /// <paramref name="chain"/> (multiclass models, Epic 2I Phase 3); null if no scope in the chain has a
    /// loaded class model. Honors a version pin (Epic 2C) exactly like
    /// <see cref="TryPredict(string, DateTimeOffset, IReadOnlyList{ModelScope}, int, out float)"/>.
    /// </summary>
    public string? TryPredictClass(string target, DateTimeOffset now, IReadOnlyList<ModelScope> chain, int pinnedVersion)
    {
        var targetKey = TargetKey(target);
        lock (_lock)
        {
            foreach (var scope in chain)
            {
                var scopeKey = scope.AsKey();
                if (!_byScope.TryGetValue(Key(target, scopeKey), out var latest) || latest.Class is null) continue;
                var serving = ResolvePinned(targetKey, scopeKey, pinnedVersion, latest) ?? latest;
                return (serving.Class ?? latest.Class)(now);
            }
        }
        return null;
    }

    // The task's soft clamp applies only to regression kinds — a binary probability is not in target units.
    private double Clamp(string targetKey, string kind, double raw)
    {
        if (kind is not (MlModelKinds.ScheduleRegression or MlModelKinds.ScheduleRegressionContext)) return raw;
        if (!_clampsByTarget.TryGetValue(targetKey, out var clamp)) return raw;
        if (clamp.Min is { } min && raw < min) return min;
        if (clamp.Max is { } max && raw > max) return max;
        return raw;
    }

    // Resolve the version-pinned predictor for a (target, scope), or null to signal "use latest". A pin of 0
    // (or one that already equals the latest version) means latest; an unloaded pin is queued for the next
    // refresh. Caller holds _lock.
    private Loaded? ResolvePinned(string targetKey, string scopeKey, int pinnedVersion, Loaded latest)
    {
        if (pinnedVersion <= 0 || latest.Meta.Version == pinnedVersion) return null;
        if (_byVersion.TryGetValue(VersionKey(targetKey, scopeKey, pinnedVersion), out var pinned)) return pinned;
        _pendingPins.TryAdd((targetKey, scopeKey, pinnedVersion), 0);
        return null;
    }
}
