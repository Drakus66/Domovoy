namespace Domovoy.Contracts.Ml;

/// <summary>
/// Spatial scope a model is trained/served for (roadmap Epic 2I). Models resolve along an ordered fallback
/// chain — most specific first — <c>zone:&lt;id&gt;</c> → <c>zone_kind:&lt;Zone.Kind&gt;</c> → <c>global</c>:
/// a thermostat in the bedroom uses the bedroom model if one exists, else the shared "living rooms" model,
/// else the house-wide model. The greenhouse (a different <see cref="Domovoy.Contracts.Ml.ModelScopeLevels"/>
/// kind) never falls back onto the house model. The level vocabulary is open and ordered, so a parent-zone or
/// archetype level can be inserted later without changing storage. Stored as a plain object for BSON.
/// </summary>
public sealed class ModelScope
{
    /// <summary>Scope level: see <see cref="ModelScopeLevels"/>.</summary>
    public string Level { get; set; } = ModelScopeLevels.Global;

    /// <summary>Scope key within the level (zone id, zone kind); empty for <see cref="ModelScopeLevels.Global"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public ModelScope() { }

    public ModelScope(string level, string key)
    {
        Level = level;
        Key = key;
    }

    public static readonly ModelScope Global = new(ModelScopeLevels.Global, string.Empty);
    public static ModelScope Zone(string zoneId) => new(ModelScopeLevels.Zone, zoneId);
    public static ModelScope ZoneKind(string kind) => new(ModelScopeLevels.ZoneKind, kind);

    /// <summary>Stable string key for dictionaries / equality (level + key).</summary>
    public string AsKey() => $"{Level}:{Key}";

    public override string ToString() => Level == ModelScopeLevels.Global ? "global" : AsKey();
}

/// <summary>Ordered scope levels, most specific first (roadmap Epic 2I). Open vocabulary.</summary>
public static class ModelScopeLevels
{
    public const string Zone = "zone";
    public const string ZoneKind = "zone_kind";
    public const string Global = "global";
}
