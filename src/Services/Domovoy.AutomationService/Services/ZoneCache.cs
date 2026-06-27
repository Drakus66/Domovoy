using System.Collections.Concurrent;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// In-memory zone id → kind lookup (roadmap Epic 2I) refreshed from the DbGateway zones read-model. Lets the
/// block runtime resolve a block instance's model-scope chain (zone → zone_kind → global) and the trainer
/// enumerate zones of a kind, without each of them re-fetching zones. Tolerant of a gateway outage — it keeps
/// the last good snapshot (offline-first, like the rest of the AutomationService).
/// </summary>
public sealed class ZoneCache
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<ZoneCache> _logger;
    private volatile IReadOnlyDictionary<string, string?> _kindById =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    public ZoneCache(DbGatewayClient db, ILogger<ZoneCache> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>The zone's kind (e.g. "room", "greenhouse"), or null if unknown/unassigned.</summary>
    public string? KindOf(string? zoneId) =>
        !string.IsNullOrEmpty(zoneId) && _kindById.TryGetValue(zoneId, out var kind) ? kind : null;

    /// <summary>Zone ids whose kind equals <paramref name="kind"/> (for kind-scoped training).</summary>
    public IReadOnlyList<string> ZonesOfKind(string kind) =>
        _kindById.Where(kv => string.Equals(kv.Value, kind, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();

    /// <summary>All known zone ids.</summary>
    public IReadOnlyList<string> AllZoneIds() => _kindById.Keys.ToList();

    /// <summary>Distinct non-empty zone kinds currently known.</summary>
    public IReadOnlyList<string> Kinds() =>
        _kindById.Values.Where(k => !string.IsNullOrEmpty(k)).Select(k => k!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Pull the latest zones; keeps the previous snapshot on failure.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var zones = await _db.GetZonesAsync(ct);
        if (zones is null) return; // keep last good

        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var z in zones)
            if (!string.IsNullOrEmpty(z.Id)) map[z.Id] = z.Kind;
        _kindById = map;
    }
}
