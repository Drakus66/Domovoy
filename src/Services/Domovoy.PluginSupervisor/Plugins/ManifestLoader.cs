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

        foreach (var folder in Directory.EnumerateDirectories(pluginsRoot))
        {
            var manifestPath = Path.Combine(folder, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), Json);
                if (manifest is null) continue;
                if (string.IsNullOrWhiteSpace(manifest.Id)) manifest.Id = Path.GetFileName(folder);
                entries.Add(new PluginEntry(manifest, folder));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read plugin manifest at {Path}", manifestPath);
            }
        }

        logger.LogInformation("Discovered {Count} plugin manifest(s) under {Root}", entries.Count, pluginsRoot);
        return entries;
    }
}
