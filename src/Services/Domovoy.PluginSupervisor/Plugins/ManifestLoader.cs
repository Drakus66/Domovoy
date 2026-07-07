using System.Text.Json;

using Domovoy.Contracts.Plugins;

namespace Domovoy.PluginSupervisor.Plugins;

/// <summary>
/// Discovers plugin manifests (roadmap Epic 1C): each plugin is a subfolder of the plugins root containing
/// a <c>plugin.json</c>. Tolerant — a bad manifest is skipped and logged, not fatal.
/// </summary>
public static class ManifestLoader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<PluginEntry> Discover(string pluginsRoot, ILogger logger)
    {
        var entries = new List<PluginEntry>();
        if (!Directory.Exists(pluginsRoot))
        {
            logger.LogInformation("Plugins root {Root} does not exist — no plugins", pluginsRoot);
            return entries;
        }

        // The transient staging folder for uploaded packages is not itself a plugin — skip it.
        foreach (var folder in Directory.EnumerateDirectories(pluginsRoot))
        {
            if (IsStagingFolder(folder)) continue;
            if (TryLoad(folder, logger, out var entry)) entries.Add(entry);
        }

        logger.LogInformation("Discovered {Count} plugin manifest(s) under {Root}", entries.Count, pluginsRoot);
        return entries;
    }

    /// <summary>Name of the transient folder uploaded packages are extracted into before being placed.</summary>
    public const string StagingFolderName = ".incoming";

    private static bool IsStagingFolder(string folder) =>
        string.Equals(Path.GetFileName(folder), StagingFolderName, StringComparison.Ordinal);

    /// <summary>
    /// Reads a single plugin folder's <c>plugin.json</c> into an entry. Returns false (and logs) when there is
    /// no manifest or it is malformed — reused by startup discovery, the upload installer and the hot-reload
    /// watcher so they parse manifests identically.
    /// </summary>
    public static bool TryLoad(string folder, ILogger logger, out PluginEntry entry)
    {
        entry = null!;
        var manifestPath = Path.Combine(folder, "plugin.json");
        if (!File.Exists(manifestPath)) return false;

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), Json);
            if (manifest is null) return false;
            if (string.IsNullOrWhiteSpace(manifest.Id)) manifest.Id = Path.GetFileName(folder);
            entry = new PluginEntry(manifest, folder);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read plugin manifest at {Path}", manifestPath);
            return false;
        }
    }
}
