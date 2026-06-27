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
    private readonly AutomationOptions _options;
    private readonly ILogger<MlModelService> _logger;

    private readonly MLContext _ml = new(seed: 0);
    private readonly object _lock = new();

    // scope key (level:key) → loaded predictor for the configured target.
    private readonly Dictionary<string, Loaded> _byScope = new(StringComparer.Ordinal);

    private sealed record Loaded(string ModelId, Func<DateTimeOffset, double> Predict, MlModel Meta);

    public MlModelService(DbGatewayClient db, IOptions<AutomationOptions> options, ILogger<MlModelService> logger)
    {
        _db = db;
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
                var predict = BuildPredictor(model, meta.Kind);
                if (predict is null)
                {
                    _logger.LogWarning("No inference for model kind {Kind} ({Id})", meta.Kind, meta.Id);
                    continue;
                }
                lock (_lock) _byScope[scopeKey] = new Loaded(meta.Id, predict, meta);
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
    }

    // Build a scalar predictor for a loaded model by kind: regression → value, binary → probability.
    private Func<DateTimeOffset, double>? BuildPredictor(ITransformer model, string kind)
    {
        switch (kind)
        {
            case MlModelKinds.ScheduleRegression:
            {
                var engine = _ml.Model.CreatePredictionEngine<MlSample, MlPrediction>(model);
                return now =>
                {
                    var (hour, dow) = Features(now);
                    return engine.Predict(new MlSample { Hour = hour, Dow = dow }).Value;
                };
            }
            case MlModelKinds.ScheduleBinary:
            {
                var engine = _ml.Model.CreatePredictionEngine<MlBinarySample, MlBinaryPrediction>(model);
                return now =>
                {
                    var (hour, dow) = Features(now);
                    return engine.Predict(new MlBinarySample { Hour = hour, Dow = dow }).Probability;
                };
            }
            default:
                return null;
        }
    }

    private static (float Hour, float Dow) Features(DateTimeOffset now) =>
        ((float)(now.Hour + now.Minute / 60.0), (float)(int)now.DayOfWeek);

    /// <summary>Predict for <paramref name="now"/> using the global model (back-compat for Epic 2A blocks).</summary>
    public bool TryPredict(DateTimeOffset now, out float value) =>
        TryPredict(now, new[] { ModelScope.Global }, out value);

    /// <summary>
    /// Predict for <paramref name="now"/> resolving the model along <paramref name="chain"/> (most specific
    /// first); false if no scope in the chain has a loaded model. The returned scalar is the model's natural
    /// output (regression value or binary probability).
    /// </summary>
    public bool TryPredict(DateTimeOffset now, IReadOnlyList<ModelScope> chain, out float value)
    {
        value = 0;
        lock (_lock)
        {
            foreach (var scope in chain)
            {
                if (!_byScope.TryGetValue(scope.AsKey(), out var loaded)) continue;
                value = (float)loaded.Predict(now);
                return true;
            }
        }
        return false;
    }
}
