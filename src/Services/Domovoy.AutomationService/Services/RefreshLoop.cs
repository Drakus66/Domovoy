using Domovoy.AutomationService.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Periodically reloads user rules and the device read-model (zone + initial state) from the DbGateway,
/// and loads the local safety floor once at startup. Keeps the engine's view fresh without each rule
/// edit needing a bus round-trip (roadmap Epic 1A; a push-invalidation event is a later optimization).
/// </summary>
public sealed class RefreshLoop : BackgroundService
{
    private readonly RuleStore _store;
    private readonly DeviceRegistry _registry;
    private readonly DbGatewayClient _db;
    private readonly HomeModeState _mode;
    private readonly AutomationOptions _options;
    private readonly ILogger<RefreshLoop> _logger;

    public RefreshLoop(
        RuleStore store, DeviceRegistry registry, DbGatewayClient db, HomeModeState mode,
        IOptions<AutomationOptions> options, ILogger<RefreshLoop> logger)
    {
        _store = store;
        _registry = registry;
        _db = db;
        _mode = mode;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _store.LoadSafetyRules();

        var period = TimeSpan.FromSeconds(Math.Max(5, _options.RefreshSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            await _store.RefreshAsync(stoppingToken);
            await RefreshDevices(stoppingToken);
            await RefreshMode(stoppingToken);

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RefreshDevices(CancellationToken ct)
    {
        var devices = await _db.GetDevicesAsync(ct);
        if (devices is null) return;

        foreach (var d in devices)
        {
            if (!Guid.TryParse(d.Id, out var id)) continue;
            _registry.SetZone(id, d.ZoneId);
            foreach (var kv in d.State)
                _registry.SeedValue(id, kv.Key, kv.Value);
        }

        _logger.LogDebug("Refreshed {Count} device(s) from read-model", devices.Count);
    }

    // Seed/repair the home mode from the DbGateway (1G). HomeModeMonitor keeps it live via the bus; this
    // also covers startup and any missed events. The gateway is the authority, so re-reading is consistent.
    private async Task RefreshMode(CancellationToken ct)
    {
        var mode = await _db.GetModeAsync(ct);
        if (mode is not null) _mode.Set(mode);
    }
}
