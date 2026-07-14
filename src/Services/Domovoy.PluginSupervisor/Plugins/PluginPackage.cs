// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.IO.Compression;
using System.Text.Json;

using Domovoy.Contracts.Plugins;

namespace Domovoy.PluginSupervisor.Plugins;

/// <summary>
/// Handles an uploaded plugin <b>package</b> (roadmap Epic 1C, UI install): a <c>.zip</c> containing a
/// <c>plugin.json</c> manifest and the plugin's binaries. Extraction is done safely (path-traversal / zip-slip
/// is rejected by <see cref="ZipFile.ExtractToDirectory(string,string)"/>), the manifest is located whether it
/// sits at the archive root or inside a single wrapping folder, and the plugin id is validated to a safe
/// folder name so a malicious archive can never write outside the plugins root. The caller (the supervisor)
/// then moves the staged folder into place and registers it.
/// </summary>
public static class PluginPackage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The result of staging a package: where it was extracted, and its parsed manifest.</summary>
    public sealed record Staged(string Folder, PluginManifest Manifest);

    /// <summary>
    /// Extracts <paramref name="zip"/> into a fresh folder under <paramref name="stagingRoot"/> and returns its
    /// manifest. Throws <see cref="InvalidPluginPackageException"/> when the archive has no valid
    /// <c>plugin.json</c> or an unsafe id. The staged folder is left for the caller to move/clean up.
    /// </summary>
    public static Staged Stage(Stream zip, string stagingRoot, string stageId)
    {
        var stageFolder = Path.Combine(stagingRoot, stageId);
        if (Directory.Exists(stageFolder)) Directory.Delete(stageFolder, recursive: true);
        Directory.CreateDirectory(stageFolder);

        try
        {
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
                archive.ExtractToDirectory(stageFolder, overwriteFiles: true); // rejects zip-slip entries

            var manifestPath = FindManifest(stageFolder)
                ?? throw new InvalidPluginPackageException("archive contains no plugin.json");

            var manifest = ReadManifest(manifestPath);
            // If the manifest lived inside a wrapping folder, that folder is the real plugin root.
            var root = Path.GetDirectoryName(manifestPath)!;

            if (string.IsNullOrWhiteSpace(manifest.Id))
                manifest.Id = Path.GetFileName(root);
            if (!IsSafeId(manifest.Id))
                throw new InvalidPluginPackageException($"unsafe plugin id '{manifest.Id}'");

            return new Staged(root, manifest);
        }
        catch
        {
            TryDelete(stageFolder);
            throw;
        }
    }

    /// <summary>A plugin id must be usable verbatim as a folder name — no separators, traversal or exotic chars.</summary>
    public static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 128
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
        && id is not "." and not ".."
        && !id.StartsWith('.'); // no hidden folders (keeps .incoming reserved)

    private static PluginManifest ReadManifest(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), Json)
                ?? throw new InvalidPluginPackageException("plugin.json is empty");
        }
        catch (JsonException ex)
        {
            throw new InvalidPluginPackageException($"plugin.json is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>Manifest at the archive root, else exactly one level down (a single wrapping folder).</summary>
    private static string? FindManifest(string stageFolder)
    {
        var atRoot = Path.Combine(stageFolder, "plugin.json");
        if (File.Exists(atRoot)) return atRoot;

        foreach (var sub in Directory.EnumerateDirectories(stageFolder))
        {
            var nested = Path.Combine(sub, "plugin.json");
            if (File.Exists(nested)) return nested;
        }
        return null;
    }

    public static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}

/// <summary>A rejected plugin package (bad zip, missing/invalid manifest, unsafe id) — a 400, not a 500.</summary>
public sealed class InvalidPluginPackageException(string message) : Exception(message);
