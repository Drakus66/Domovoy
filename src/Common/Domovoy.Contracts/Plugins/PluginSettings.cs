namespace Domovoy.Contracts.Plugins;

/// <summary>
/// A single configurable setting a plugin exposes to the UI (roadmap Epic 2M tail — plugin settings).
/// This is the plugin's <b>configuration</b>, not a device capability: settings drive how the plugin runs
/// (traffic backend, API keys, tuning) and deliberately do NOT surface as devices in the dashboard or rules.
/// The metadata vocabulary intentionally mirrors <c>CapabilityAttributeKeys</c>/<c>CapabilityEditors</c> so the
/// WebUI can render the same switches/sliders/selects it already renders for writable capabilities.
/// </summary>
public sealed class PluginSettingDescriptor
{
    /// <summary>Stable key the value is stored/looked-up under (e.g. <c>"provider"</c>). Also the config path.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Value shape — one of <see cref="PluginSettingKinds"/> (boolean/number/enum/text/secret).</summary>
    public string Kind { get; set; } = PluginSettingKinds.Text;

    /// <summary>Human label for the control (plugin-provided; already localized by the plugin author).</summary>
    public string? Label { get; set; }

    /// <summary>Optional longer help text shown under the control.</summary>
    public string? Description { get; set; }

    /// <summary>Default value used until the operator changes it (also the value shown for a fresh install).</summary>
    public object? Default { get; set; }

    /// <summary>Minimum for a <c>number</c> setting (drives the slider range).</summary>
    public double? Min { get; set; }

    /// <summary>Maximum for a <c>number</c> setting (drives the slider range).</summary>
    public double? Max { get; set; }

    /// <summary>Step for a <c>number</c> setting.</summary>
    public double? Step { get; set; }

    /// <summary>Unit suffix for a <c>number</c> setting (e.g. <c>"km/h"</c>, <c>"min"</c>).</summary>
    public string? Unit { get; set; }

    /// <summary>Allowed values for an <c>enum</c> setting (drives the dropdown).</summary>
    public List<string>? Values { get; set; }

    /// <summary>Optional UI editor hint reused from capabilities (<c>"geo"</c>, <c>"time"</c>).</summary>
    public string? Editor { get; set; }

    /// <summary>Secret value (API key/token): masked on read, only overwritten when a new value is sent.</summary>
    public bool Secret { get; set; }
}

/// <summary>Well-known <see cref="PluginSettingDescriptor.Kind"/> values (lower-case, mirroring capability kinds).</summary>
public static class PluginSettingKinds
{
    public const string Boolean = "boolean";
    public const string Number = "number";
    public const string Enum = "enum";
    public const string Text = "text";

    /// <summary>Text-like secret (API key/token) — the UI renders a password field and the value is masked on read.</summary>
    public const string Secret = "secret";
}

/// <summary>
/// Plugin → supervisor: the plugin announces the settings it exposes (schema + defaults + ranges) on startup.
/// The supervisor caches this to drive the settings button/dialog on the plugins panel and, in return,
/// publishes the operator's currently-saved values back so the freshly-started plugin adopts its configuration.
/// </summary>
public sealed record PluginSettingsSchemaV1(
    string PluginId,
    IReadOnlyList<PluginSettingDescriptor> Settings);

/// <summary>
/// Supervisor → plugin: the effective values to apply, published both as the reply to a schema announce
/// (initial configuration) and whenever the operator saves changes in the UI — the plugin applies them live.
/// </summary>
public sealed record PluginSettingsAppliedV1(
    string PluginId,
    IReadOnlyDictionary<string, object?> Values);
