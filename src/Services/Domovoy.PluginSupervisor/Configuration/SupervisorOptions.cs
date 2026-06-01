namespace Domovoy.PluginSupervisor.Configuration;

/// <summary>
/// Plugin supervisor configuration (roadmap Epic 1C). Bound from the <c>Supervisor</c> config section.
/// </summary>
public class SupervisorOptions
{
    public const string SectionName = "Supervisor";

    /// <summary>Root folder scanned for plugins; each plugin lives in a subfolder with a <c>plugin.json</c>.</summary>
    public string PluginsRoot { get; set; } = "plugins";

    /// <summary>Whether this host has a usable GPU (resource gating). Declared, not auto-detected.</summary>
    public bool GpuAvailable { get; set; } = false;

    /// <summary>Whether this host has internet access (gates cloud plugins).</summary>
    public bool InternetAvailable { get; set; } = true;

    /// <summary>Total memory advertised to plugins for gating, MB. 0 = derive from the runtime.</summary>
    public int MemoryMbOverride { get; set; } = 0;

    /// <summary>Seconds to wait before restarting a crashed plugin (capped exponential backoff base).</summary>
    public int RestartBackoffSeconds { get; set; } = 10;

    /// <summary>Give up auto-restarting after this many consecutive crashes.</summary>
    public int MaxRestarts { get; set; } = 5;
}
