using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Ml;

using Microsoft.Extensions.Options;
using Microsoft.ML;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Loads and serves the latest registered model for inference (roadmap Epic 2A). Downloads the model
/// artifact from the DbGateway registry, holds a cached ML.NET <see cref="PredictionEngine{TSrc,TDst}"/>,
/// and offers a synchronous <see cref="TryPredict"/> for the (single-threaded) block tick. The ML block
/// (1H) calls this; refresh happens in the background, so the block emits nothing until a model is loaded.
/// </summary>
public sealed class MlModelService
{
    private readonly DbGatewayClient _db;
    private readonly AutomationOptions _options;
    private readonly ILogger<MlModelService> _logger;

    private readonly MLContext _ml = new(seed: 0);
    private readonly object _lock = new();
    private PredictionEngine<MlSample, MlPrediction>? _engine;
    private string? _loadedId;

    public MlModelService(DbGatewayClient db, IOptions<AutomationOptions> options, ILogger<MlModelService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Metadata of the currently loaded model, if any.</summary>
    public MlModel? Current { get; private set; }

    /// <summary>Pull the latest registered model and (re)load its engine if the version changed.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var latest = await _db.GetLatestModelAsync(MlModelKinds.ScheduleRegression, _options.TrainCapability, ct);
        if (latest is null || latest.Id == _loadedId) return;

        var bytes = await _db.GetModelArtifactAsync(latest.Id, ct);
        if (bytes is null) return;

        try
        {
            using var stream = new MemoryStream(bytes);
            var model = _ml.Model.Load(stream, out _);
            var engine = _ml.Model.CreatePredictionEngine<MlSample, MlPrediction>(model);
            lock (_lock)
            {
                _engine = engine;
                _loadedId = latest.Id;
                Current = latest;
            }
            _logger.LogInformation("Loaded ML model {Name} v{Version} (rmse {Rmse:0.###})", latest.Name, latest.Version, latest.Rmse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ML model {Id}", latest.Id);
        }
    }

    /// <summary>Predict the target value for <paramref name="now"/>; false if no model is loaded.</summary>
    public bool TryPredict(DateTimeOffset now, out float value)
    {
        value = 0;
        lock (_lock)
        {
            if (_engine is null) return false;
            var p = _engine.Predict(new MlSample
            {
                Hour = (float)(now.Hour + now.Minute / 60.0),
                Dow = (float)(int)now.DayOfWeek,
            });
            value = p.Value;
            return true;
        }
    }
}
