using Domovoy.AutomationService.Configuration;
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
    private readonly MlTrainer _trainer;
    private readonly MlModelService _models;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlTrainingService> _logger;

    private DateTime _lastTrain = DateTime.MinValue;

    public MlTrainingService(
        DbGatewayClient db, MlTrainer trainer, MlModelService models,
        IOptions<AutomationOptions> options, ILogger<MlTrainingService> logger)
    {
        _db = db;
        _trainer = trainer;
        _models = models;
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

    /// <summary>Train once on recent telemetry, register the model and reload it for inference.</summary>
    public async Task<TrainResult> TrainOnceAsync(CancellationToken ct)
    {
        _lastTrain = DateTime.UtcNow;
        var from = DateTime.UtcNow.AddDays(-_options.TrainWindowDays);
        var samples = await _db.GetTelemetryAsync(_options.TrainCapability, from, 5000, ct);
        if (samples is null)
            return new TrainResult(false, "telemetry unavailable", null);
        if (samples.Count < _options.MinSamples)
            return new TrainResult(false, $"not enough data ({samples.Count}/{_options.MinSamples})", null);

        var result = _trainer.Train(samples.Select(s => (s.Timestamp, s.Value)).ToList(), _options.MinSamples);
        if (result is null)
            return new TrainResult(false, "training produced no model", null);

        var model = new MlModel
        {
            Name = $"{_options.TrainCapability} schedule",
            Kind = MlModelKinds.ScheduleRegression,
            TargetCapability = _options.TrainCapability,
            SampleCount = result.SampleCount,
            Rmse = result.Rmse,
            HoldoutMae = result.HoldoutMae,
            HoldoutSampleCount = result.HoldoutCount,
            Algorithm = MlTrainer.Algorithm,
        };
        var registered = await _db.RegisterModelAsync(model, result.Artifact, ct);
        if (registered is null)
            return new TrainResult(false, "could not register model", null);

        await _models.RefreshAsync(ct);
        _logger.LogInformation("Trained {Name} on {Count} samples (rmse {Rmse:0.###})", model.Name, result.SampleCount, result.Rmse);
        return new TrainResult(true, "ok", registered);
    }
}
