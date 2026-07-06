using System.Collections.Concurrent;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml.Templates;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Options;
using Microsoft.ML;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Loads and serves registered models for inference (roadmap Epic 2A; per-zone scoping Epic 2I; multi-kind
/// Phase 2). It holds a cached scalar predictor <b>per scope</b> (zone / zone-kind / global) of the configured
/// target, refreshed in the background, and offers a synchronous
/// <see cref="TryPredict(DateTimeOffset, IReadOnlyList{ModelScope}, out float)"/> for the (single-threaded)
/// block tick that resolves a model along the instance's fallback chain. The predictor's scalar is the model's
/// natural output — a value for a regression (setpoint) model, a probability for a binary (toggle) model — so
/// the consuming governor interprets it for its value-type. Emits nothing until some model is loaded.
/// </summary>
public sealed class MlModelService
{
    private readonly DbGatewayClient _db;
    private readonly HomeModeState _mode;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlModelService> _logger;

    private readonly MLContext _ml = new(seed: 0);
    private readonly object _lock = new();

    // scope key (level:key) → loaded predictor for the configured target (latest version of each scope).
    private readonly Dictionary<string, Loaded> _byScope = new(StringComparer.Ordinal);

    // Version-pinned predictors (Epic 2C model_selection): "{scopeKey}#v{version}" → loaded predictor. Populated
    // lazily — a tick asking for a version we haven't loaded registers it in _pendingPins, the next background
    // refresh fetches it, and until then the instance falls back to the scope's latest model. Keeps the sync
    // block tick allocation-free while the (async) artifact download stays on the refresh thread.
    private readonly Dictionary<string, Loaded> _byVersion = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string ScopeKey, int Version), byte> _pendingPins = new();

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

    /// <summary>Metadata of the loaded global model, if any (back-compat: scorecard / status).</summary>
    public MlModel? Current { get; private set; }

    /// <summary>
    /// (Re)load the per-scope predictors for the configured target from the registry. Loads the latest version
    /// of each scope that has a model and drops scopes whose model disappeared. Any model kind is served.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var all = await _db.GetModelsAsync(ct);
        if (all is null) return;

        // Latest version per scope, for the configured target (any kind).
        var latestByScope = all
            .Where(m => string.Equals(m.TargetCapability, _options.TrainCapability, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => (m.Scope ?? ModelScope.Global).AsKey(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Version).First(), StringComparer.Ordinal);

        foreach (var (scopeKey, meta) in latestByScope)
        {
            lock (_lock)
            {
                if (_byScope.TryGetValue(scopeKey, out var loaded) && loaded.ModelId == meta.Id)
                    continue; // already current
            }

            var bytes = await _db.GetModelArtifactAsync(meta.Id, ct);
            if (bytes is null) continue;

            try
            {
                using var stream = new MemoryStream(bytes);
                var model = _ml.Model.Load(stream, out _);
                var (scalar, klass) = BuildPredictors(model, meta.Kind);
                if (scalar is null && klass is null)
                {
                    _logger.LogWarning("No inference for model kind {Kind} ({Id})", meta.Kind, meta.Id);
                    continue;
                }
                lock (_lock) _byScope[scopeKey] = new Loaded(meta.Id, scalar, klass, meta);
                _logger.LogInformation(
                    "Loaded ML model {Name} v{Version} scope {Scope} ({Metric} {Score:0.###})",
                    meta.Name, meta.Version, meta.Scope, meta.Metric, meta.HoldoutScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load ML model {Id}", meta.Id);
            }
        }

        lock (_lock)
        {
            // Drop scopes whose model is gone, and refresh the back-compat Current pointer.
            foreach (var stale in _byScope.Keys.Where(k => !latestByScope.ContainsKey(k)).ToList())
                _byScope.Remove(stale);
            Current = _byScope.TryGetValue(ModelScope.Global.AsKey(), out var g) ? g.Meta
                : _byScope.Values.FirstOrDefault()?.Meta;
        }

        await LoadPendingPinsAsync(all, ct);
    }

    // Fetch any version pins a block tick has requested (Epic 2C). A pin for a version that doesn't exist in the
    // registry is dropped (the instance keeps using latest); a transient artifact-fetch failure is retried next
    // refresh. Loaded pins persist across refreshes (the set is tiny — one per pinned instance).
    private async Task LoadPendingPinsAsync(List<MlModel> all, CancellationToken ct)
    {
        foreach (var pin in _pendingPins.Keys.ToList())
        {
            var versionKey = VersionKey(pin.ScopeKey, pin.Version);
            lock (_lock)
            {
                if (_byVersion.ContainsKey(versionKey)) { _pendingPins.TryRemove(pin, out _); continue; }
            }

            var meta = all.FirstOrDefault(m =>
                string.Equals(m.TargetCapability, _options.TrainCapability, StringComparison.OrdinalIgnoreCase)
                && (m.Scope ?? ModelScope.Global).AsKey() == pin.ScopeKey
                && m.Version == pin.Version);
            if (meta is null) { _pendingPins.TryRemove(pin, out _); continue; } // no such version — keep using latest

            var bytes = await _db.GetModelArtifactAsync(meta.Id, ct);
            if (bytes is null) continue; // transient — retry next refresh

            try
            {
                using var stream = new MemoryStream(bytes);
                var model = _ml.Model.Load(stream, out _);
                var (scalar, klass) = BuildPredictors(model, meta.Kind);
                if (scalar is null && klass is null) { _pendingPins.TryRemove(pin, out _); continue; }
                lock (_lock) _byVersion[versionKey] = new Loaded(meta.Id, scalar, klass, meta);
                _pendingPins.TryRemove(pin, out _);
                _logger.LogInformation("Pinned ML model {Name} v{Version} scope {Scope} loaded (Epic 2C)",
                    meta.Name, meta.Version, meta.Scope);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load pinned ML model v{Version}", pin.Version);
                _pendingPins.TryRemove(pin, out _);
            }
        }
    }

    private static string VersionKey(string scopeKey, int version) => $"{scopeKey}#v{version}";

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

    /// <summary>Predict for <paramref name="now"/> using the global model (back-compat for Epic 2A blocks).</summary>
    public bool TryPredict(DateTimeOffset now, out float value) =>
        TryPredict(now, new[] { ModelScope.Global }, 0, out value);

    /// <summary>Predict along <paramref name="chain"/> using each scope's latest model (back-compat overload).</summary>
    public bool TryPredict(DateTimeOffset now, IReadOnlyList<ModelScope> chain, out float value) =>
        TryPredict(now, chain, 0, out value);

    /// <summary>
    /// Predict for <paramref name="now"/> resolving the model along <paramref name="chain"/> (most specific
    /// first); false if no scope in the chain has a loaded model. The returned scalar is the model's natural
    /// output (regression value or binary probability). When <paramref name="pinnedVersion"/> &gt; 0 the serving
    /// scope's pinned version is used if loaded (Epic 2C model_selection); otherwise the pin is queued for the
    /// next refresh and the scope's latest model is used meanwhile.
    /// </summary>
    public bool TryPredict(DateTimeOffset now, IReadOnlyList<ModelScope> chain, int pinnedVersion, out float value)
    {
        value = 0;
        lock (_lock)
        {
            foreach (var scope in chain)
            {
                var scopeKey = scope.AsKey();
                if (!_byScope.TryGetValue(scopeKey, out var latest) || latest.Scalar is null) continue;
                var predictor = ResolvePinned(scopeKey, pinnedVersion, latest)?.Scalar ?? latest.Scalar;
                value = (float)predictor(now);
                return true;
            }
        }
        return false;
    }

    /// <summary>Predict the class label along <paramref name="chain"/> using each scope's latest model.</summary>
    public string? TryPredictClass(DateTimeOffset now, IReadOnlyList<ModelScope> chain) =>
        TryPredictClass(now, chain, 0);

    /// <summary>
    /// Predict the class label for <paramref name="now"/> along the scope <paramref name="chain"/> (multiclass
    /// models, Epic 2I Phase 3); null if no scope in the chain has a loaded class model. Honors a version pin
    /// (Epic 2C) exactly like <see cref="TryPredict(DateTimeOffset, IReadOnlyList{ModelScope}, int, out float)"/>.
    /// </summary>
    public string? TryPredictClass(DateTimeOffset now, IReadOnlyList<ModelScope> chain, int pinnedVersion)
    {
        lock (_lock)
        {
            foreach (var scope in chain)
            {
                var scopeKey = scope.AsKey();
                if (!_byScope.TryGetValue(scopeKey, out var latest) || latest.Class is null) continue;
                var predictor = ResolvePinned(scopeKey, pinnedVersion, latest)?.Class ?? latest.Class;
                return predictor(now);
            }
        }
        return null;
    }

    // Resolve the version-pinned predictor for a scope, or null to signal "use latest". A pin of 0 (or one that
    // already equals the latest version) means latest; an unloaded pin is queued for the next refresh. Caller
    // holds _lock.
    private Loaded? ResolvePinned(string scopeKey, int pinnedVersion, Loaded latest)
    {
        if (pinnedVersion <= 0 || latest.Meta.Version == pinnedVersion) return null;
        if (_byVersion.TryGetValue(VersionKey(scopeKey, pinnedVersion), out var pinned)) return pinned;
        _pendingPins.TryAdd((scopeKey, pinnedVersion), 0);
        return null;
    }
}
