// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

namespace Domovoy.PluginSupervisor.Plugins;

/// <summary>
/// Delivers the first-party plugins that ship inside this image into the plugins root (roadmap Epic 1C
/// plugins × Epic 3K delivery).
///
/// <para><b>Why the supervisor carries them.</b> A plugin is a folder on the host volume, not a compose
/// service, so before this it reached a house only if somebody copied the DLLs there by hand — which meant
/// the update channel could not deliver a plugin fix at all. Baking the first-party plugins into the
/// supervisor image makes them travel with the component that runs them: one image, one declared version,
/// one update.</para>
///
/// <para><b>Uninstall is respected.</b> A seeded id is recorded in <c>.builtin-state.json</c> at the plugins
/// root. The rules are deliberately narrow:</para>
/// <list type="bullet">
///   <item>never seeded → install it (fresh house gets the first-party set);</item>
///   <item>seeded, folder present, image carries a newer version → refresh in place;</item>
///   <item>seeded, folder gone → the operator uninstalled it; leave it gone.</item>
/// </list>
/// <para>Otherwise "uninstall" would mean "until the next restart", which is not an uninstall.</para>
/// </summary>
public static class BuiltInPluginSeeder
{
    /// <summary>Where the image keeps the published first-party plugins (see the Dockerfile).</summary>
    public const string DefaultSourceDirectory = "/app/builtin-plugins";

    private const string StateFile = ".builtin-state.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Syncs every plugin folder under <paramref name="sourceDirectory"/> into
    /// <paramref name="pluginsRoot"/>. Returns the ids it actually wrote — the caller logs them; discovery
    /// then picks them up as ordinary plugins, with no special case anywhere downstream.
    /// </summary>
    public static IReadOnlyList<string> Seed(string sourceDirectory, string pluginsRoot, ILogger logger)
    {
        if (!Directory.Exists(sourceDirectory)) return Array.Empty<string>();

        Directory.CreateDirectory(pluginsRoot);

        var state = ReadState(pluginsRoot, logger);
        var seeded = new List<string>();

        foreach (var source in Directory.EnumerateDirectories(sourceDirectory))
        {
            var id = Path.GetFileName(source);
            var available = VersionOf(source);
            if (available is null)
            {
                logger.LogWarning("Встроенный плагин {Id} без пригодного plugin.json — пропущен", id);
                continue;
            }

            var target = Path.Combine(pluginsRoot, id);
            var installed = Directory.Exists(target) ? VersionOf(target) : null;

            if (!ShouldSeed(state.ContainsKey(id), installed, available)) continue;

            try
            {
                CopyOver(source, target);
                state[id] = available;
                seeded.Add(id);
                logger.LogInformation(
                    "Встроенный плагин {Id} разложен в {Target}: {From} → {To}",
                    id, target, installed ?? "—", available);
            }
            catch (Exception ex)
            {
                // A plugin that fails to unpack must not stop the supervisor: the rest of the house works
                // without it, and discovery simply won't find it.
                logger.LogError(ex, "Не удалось разложить встроенный плагин {Id}", id);
            }
        }

        if (seeded.Count > 0) WriteState(pluginsRoot, state, logger);
        return seeded;
    }

    /// <summary>The delivery-vs-uninstall rule, isolated so it can be stated once and tested directly.</summary>
    public static bool ShouldSeed(bool seenBefore, string? installed, string available)
    {
        if (!seenBefore) return true;                 // свежий дом — раскладываем первопартийный набор
        if (installed is null) return false;          // владелец удалил плагин — это его решение
        return IsNewer(available, installed);
    }

    /// <summary>
    /// Numeric-segment comparison, enough for the <c>MAJOR.MINOR.PATCH</c> manifests actually ship with.
    /// An unparseable segment sorts as 0 rather than throwing — a malformed version must not stop delivery.
    /// </summary>
    public static bool IsNewer(string candidate, string current)
    {
        var a = Segments(candidate);
        var b = Segments(current);

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }

        return false;
    }

    private static int[] Segments(string version) => version
        .Split('-', '+')[0]
        .Split('.')
        .Select(s => int.TryParse(s, out var n) ? n : 0)
        .ToArray();

    private static string? VersionOf(string pluginFolder)
    {
        var manifest = Path.Combine(pluginFolder, "plugin.json");
        if (!File.Exists(manifest)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Copies the published folder over the installed one. Files the operator added stay: only what the
    /// image carries is overwritten, so a hand-tweaked settings file next to the DLLs survives an update.
    /// </summary>
    private static void CopyOver(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static Dictionary<string, string> ReadState(string pluginsRoot, ILogger logger)
    {
        var path = Path.Combine(pluginsRoot, StateFile);
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // Unreadable state would re-seed an uninstalled plugin, so treat it as "everything seeded"
            // rather than "nothing": the conservative direction here is to do less, not more.
            logger.LogWarning(ex, "Состояние встроенных плагинов нечитаемо — считаем всё уже разложенным");
            return Directory.EnumerateDirectories(pluginsRoot)
                .ToDictionary(Path.GetFileName!, d => VersionOf(d) ?? "0", StringComparer.Ordinal);
        }
    }

    private static void WriteState(string pluginsRoot, Dictionary<string, string> state, ILogger logger)
    {
        try
        {
            File.WriteAllText(Path.Combine(pluginsRoot, StateFile), JsonSerializer.Serialize(state, Json));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось сохранить состояние встроенных плагинов");
        }
    }
}
