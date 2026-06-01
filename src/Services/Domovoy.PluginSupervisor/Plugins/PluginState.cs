using System.Diagnostics;
using System.Text.Json.Serialization;

using Domovoy.Contracts.Plugins;

namespace Domovoy.PluginSupervisor.Plugins;

/// <summary>Lifecycle status of a plugin in the supervisor (roadmap Epic 1C).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginStatus
{
    /// <summary>Manifest loaded, not yet started.</summary>
    Discovered,
    /// <summary>Host can't satisfy the declared resources — will not run (resource-aware gating).</summary>
    Blocked,
    /// <summary>Turned off in the manifest.</summary>
    Disabled,
    /// <summary>Process is starting.</summary>
    Starting,
    /// <summary>Process is up.</summary>
    Running,
    /// <summary>Stopped by an operator.</summary>
    Stopped,
    /// <summary>Process crashed / exited unexpectedly (and exhausted restarts).</summary>
    Failed,
}

/// <summary>
/// Runtime entry for one plugin: its manifest plus live supervision state. The <see cref="Process"/> is
/// out-of-process, so a plugin crash is isolated from the core services (roadmap Epic 1C DoD).
/// </summary>
public sealed class PluginEntry
{
    public PluginEntry(PluginManifest manifest, string folder)
    {
        Manifest = manifest;
        Folder = folder;
    }

    public PluginManifest Manifest { get; }
    public string Folder { get; }

    public PluginStatus Status { get; set; } = PluginStatus.Discovered;
    public string? Detail { get; set; }
    public int RestartCount { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastExitAt { get; set; }

    /// <summary>When a crashed plugin is due to be restarted (backoff); null if not pending.</summary>
    [JsonIgnore] public DateTime? NextRestartAt { get; set; }

    /// <summary>True once an operator stopped it — suppresses auto-restart until started again.</summary>
    [JsonIgnore] public bool StoppedByOperator { get; set; }

    [JsonIgnore] public Process? Process { get; set; }

    public bool IsAlive => Process is { HasExited: false };
}
