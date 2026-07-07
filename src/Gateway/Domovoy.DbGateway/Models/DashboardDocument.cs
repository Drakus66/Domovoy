using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// A user-defined dashboard tab on the main page (custom dashboards epic). The main page shows
/// "All" (the classic zone-grouped view) plus auto-generated sphere tabs (light/climate/… derived
/// client-side from device archetypes) plus these hand-curated tabs. One household — no per-user
/// scoping until auth lands in Phase 3. Persisted as plain BSON in the <c>dashboards</c> collection
/// (no contract dependency — a pure presentation/read-model concern, like <see cref="Zone"/>).
/// </summary>
public class DashboardDocument
{
    /// <summary>Stable dashboard id (GUID as string). Generated on create.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Key into the WebUI's small curated icon set; null → default icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Tab order among custom dashboards; lower comes first.</summary>
    public int Order { get; set; }

    /// <summary>Ordered named sections; item order inside a section is the list order.</summary>
    public List<DashboardSection> Sections { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DashboardSection
{
    public string Title { get; set; } = string.Empty;

    public List<DashboardItem> Items { get; set; } = new();
}

/// <summary>
/// One tile/widget on a custom dashboard. <see cref="Type"/> is one of
/// <c>device</c> (whole-device tile), <c>capability</c> (single capability of a device),
/// <c>chart</c> (telemetry chart for a device capability), <c>modes</c> (home-mode switcher).
/// </summary>
public class DashboardItem
{
    public string Type { get; set; } = "device";

    public string? DeviceId { get; set; }

    /// <summary>Capability id for <c>capability</c>/<c>chart</c> items.</summary>
    public string? CapabilityId { get; set; }

    /// <summary>
    /// Open per-type extras (e.g. chart: <c>hours</c>, <c>bucket</c>). Values arrive as
    /// <c>JsonElement</c> over HTTP and round-trip via the registered JsonObjectDictionarySerializer.
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
}

/// <summary>
/// Household-wide dashboard preferences singleton (<c>dashboard_prefs</c> collection): which
/// auto-sphere tabs the user has hidden. Follows the SiteLocation singleton pattern.
/// </summary>
public class DashboardPrefs
{
    public const string SingletonId = "current";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    /// <summary>Hidden sphere category keys (light/switch/climate/sensor/security/energy/other).</summary>
    public List<string> HiddenSpheres { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
