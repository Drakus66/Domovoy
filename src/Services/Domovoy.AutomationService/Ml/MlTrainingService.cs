using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml.Templates;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Orchestrates the ML substrate (roadmap Epic 2A): periodically trains the schedule model from the
/// telemetry feature store (P0-5/1B) and registers it in the DbGateway registry, and keeps the inference
/// <see cref="MlModelService"/> loaded with the latest model. Training also runs on demand via
/// <c>POST /api/ml/train</c>. Registered as a hosted service + injectable for the endpoint.
/// </summary>
public sealed class MlTrainingService : BackgroundService
{
    private readonly DbGatewayClient _db;
    private readonly ModelTemplateRegistry _templates;
    private readonly MlModelService _models;
    private readonly ZoneCache _zones;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlTrainingService> _logger;

    private DateTime _lastTrain = DateTime.MinValue;

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

    /// <summary>One backtest point for the scorecard chart (roadmap Epic 2B): model prediction vs reality.</summary>
    public sealed record BacktestPoint(DateTime Timestamp, double Predicted, double Actual);

    /// <summary>The scorecard payload: the loaded model's metadata + a predicted-vs-actual series.</summary>
    public sealed record Backtest(MlModel? Model, IReadOnlyList<BacktestPoint> Points);

    /// <summary>
    /// Score the currently loaded model against recent telemetry (roadmap Epic 2B) — "prediction vs fact".
    /// Returns the loaded model's metadata plus a point series the WebUI overlays. Empty series if no model
    /// is loaded or telemetry is unavailable.
    /// </summary>
    public async Task<Backtest> BacktestAsync(int days, CancellationToken ct)
    {
        var model = _models.Current;
        if (model is null) return new Backtest(null, Array.Empty<BacktestPoint>());

        var from = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        var samples = await _db.GetTelemetryAsync(_options.TrainCapability, from, 5000, ct);
        if (samples is null || samples.Count == 0) return new Backtest(model, Array.Empty<BacktestPoint>());

        var points = new List<BacktestPoint>(samples.Count);
        foreach (var s in samples.OrderBy(s => s.Timestamp))
        {
            if (_models.TryPredict(new DateTimeOffset(s.Timestamp, TimeSpan.Zero), out var predicted))
                points.Add(new BacktestPoint(s.Timestamp, Math.Round(predicted, 2), Math.Round(s.Value, 2)));
        }
        return new Backtest(model, points);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _models.RefreshAsync(stoppingToken); // pick up any pre-existing model

        var refresh = TimeSpan.FromMinutes(Math.Max(1, _options.ModelRefreshMinutes));
        using var timer = new PeriodicTimer(refresh);
        while (!stoppingToken.IsCancellationRequested)
        {
            if ((DateTime.UtcNow - _lastTrain).TotalHours >= _options.TrainIntervalHours)
            {
                try { await TrainOnceAsync(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Scheduled training failed"); }
            }
            await _models.RefreshAsync(stoppingToken);
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    /// <summary>
    /// Train once on recent telemetry and register models, then reload them for inference. Always fits the
    /// global model; when <see cref="AutomationOptions.TrainZoneModels"/> is on, also fits shared per-zone-kind
    /// models and, where a zone's own data beats its fallback by the promotion margin, per-zone models
    /// (roadmap Epic 2I). The returned <see cref="TrainResult.Model"/> is the global model (the always-present
    /// one); zone scopes are best-effort and never fail the global train.
    /// </summary>
    public async Task<TrainResult> TrainOnceAsync(CancellationToken ct)
    {
        _lastTrain = DateTime.UtcNow;
        var from = DateTime.UtcNow.AddDays(-_options.TrainWindowDays);

        var candidates = _templates.ForTarget(CapabilityKindResolver.KindOf(_options.TrainCapability));
        if (candidates.Count == 0)
            return new TrainResult(false, $"no template for {_options.TrainCapability}", null);

        // 1) Global model — always trained; its absence fails the whole run.
        var global = await _db.GetTelemetryAsync(_options.TrainCapability, from, 5000, ct);
        if (global is null)
            return new TrainResult(false, "telemetry unavailable", null);
        if (global.Count < _options.MinSamples)
            return new TrainResult(false, $"not enough data ({global.Count}/{_options.MinSamples})", null);

        var selected = SelectBest(candidates, ToLabeled(global));
        if (selected is null)
            return new TrainResult(false, "training produced no model", null);

        var registeredGlobal = await RegisterAsync(selected.Value, ModelScope.Global, ct);
        if (registeredGlobal is null)
            return new TrainResult(false, "could not register model", null);

        // 2) Per-zone scopes (Epic 2I) — best-effort.
        var zoneModels = _options.TrainZoneModels
            ? await TrainZoneScopesAsync(candidates, from, selected.Value.Result.HoldoutScore, ct)
            : 0;

        await _models.RefreshAsync(ct);
        _logger.LogInformation(
            "Trained {Name} on {Count} samples ({Metric} {Score:0.###}); +{Zones} zone model(s)",
            registeredGlobal.Name, selected.Value.Result.SampleCount, registeredGlobal.Metric,
            registeredGlobal.HoldoutScore, zoneModels);
        return new TrainResult(true, zoneModels > 0 ? $"ok (+{zoneModels} zone models)" : "ok", registeredGlobal);
    }

    // Fit shared zone-kind models (the fallbacks) and per-zone models that beat their fallback by the margin.
    private async Task<int> TrainZoneScopesAsync(
        IReadOnlyList<IModelTemplate> candidates, DateTime from, double globalScore, CancellationToken ct)
    {
        var registered = 0;
        var kindFallbackScore = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Shared per-zone-kind models (always registered when they train — they are the fallbacks).
        foreach (var kind in _zones.Kinds())
        {
            var samples = await CollectZoneTelemetry(_zones.ZonesOfKind(kind), from, ct);
            if (samples.Count < _options.MinSamples) continue;

            var sel = SelectBest(candidates, samples);
            if (sel is null) continue;

            if (await RegisterAsync(sel.Value, ModelScope.ZoneKind(kind), ct) is not null)
            {
                kindFallbackScore[kind] = sel.Value.Result.HoldoutScore;
                registered++;
            }
        }

        // Per-zone models — promoted only when they beat their fallback (zone_kind, else global).
        foreach (var zoneId in _zones.AllZoneIds())
        {
            var samples = await CollectZoneTelemetry(new[] { zoneId }, from, ct);
            if (samples.Count < _options.MinSamples) continue;

            var sel = SelectBest(candidates, samples);
            if (sel is null) continue;

            var kind = _zones.KindOf(zoneId);
            var fallback = kind is not null && kindFallbackScore.TryGetValue(kind, out var f) ? f : globalScore;
            if (!ModelTemplateRegistry.ShouldPromote(sel.Value.Template, sel.Value.Result.HoldoutScore, fallback, _options.ZonePromotionMargin))
                continue;

            if (await RegisterAsync(sel.Value, ModelScope.Zone(zoneId), ct) is not null)
                registered++;
        }

        return registered;
    }

    // Best applicable template by honest holdout (model selection, Epic 2I), or null if none trains.
    private (IModelTemplate Template, TemplateResult Result)? SelectBest(
        IReadOnlyList<IModelTemplate> candidates, IReadOnlyList<LabeledSample> labeled)
    {
        (IModelTemplate Template, TemplateResult Result)? best = null;
        foreach (var template in candidates)
        {
            var r = template.Train(labeled, _options.MinSamples);
            if (r is null) continue;
            if (best is null || ModelTemplateRegistry.IsBetter(template, r.HoldoutScore, best.Value.Result.HoldoutScore))
                best = (template, r);
        }
        return best;
    }

    private async Task<MlModel?> RegisterAsync(
        (IModelTemplate Template, TemplateResult Result) selected, ModelScope scope, CancellationToken ct)
    {
        var (template, r) = selected;
        var model = new MlModel
        {
            Name = $"{_options.TrainCapability} {template.Kind} [{scope}]",
            Kind = template.Kind,
            TargetCapability = _options.TrainCapability,
            Scope = scope,
            SampleCount = r.SampleCount,
            Rmse = r.InSampleError,
            HoldoutMae = template.Metric == "MAE" ? r.HoldoutScore : 0, // back-compat (regression only)
            HoldoutScore = r.HoldoutScore,
            HoldoutSampleCount = r.HoldoutCount,
            Metric = template.Metric,
            Features = "time",
            Algorithm = template.Algorithm,
        };
        return await _db.RegisterModelAsync(model, r.Artifact, ct);
    }

    private async Task<List<LabeledSample>> CollectZoneTelemetry(
        IReadOnlyList<string> zoneIds, DateTime from, CancellationToken ct)
    {
        var all = new List<LabeledSample>();
        foreach (var zoneId in zoneIds)
        {
            var s = await _db.GetTelemetryAsync(_options.TrainCapability, from, 5000, ct, zoneId);
            if (s is not null) all.AddRange(ToLabeled(s));
        }
        return all;
    }

    private static List<LabeledSample> ToLabeled(IEnumerable<DbGatewayClient.TelemetrySample> samples) =>
        samples.Select(s => new LabeledSample(s.Timestamp, s.Value)).ToList();
}
