// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;
using System.Diagnostics;

using Domovoy.PluginSupervisor.Configuration;
using Domovoy.PluginSupervisor.Resources;

using Microsoft.Extensions.Options;

namespace Domovoy.PluginSupervisor.Plugins;

/// <summary>
/// Registry + supervisor for integration plugins (roadmap Epic 1C). On startup it discovers manifests,
/// gates each on host resources (resource-aware modularity), and auto-starts the satisfiable ones as
/// <b>separate OS processes</b> — so a plugin crash is isolated and never takes down the core. A periodic
/// monitor detects exits and restarts crashed plugins with capped backoff. Operators can start/stop
/// plugins via the API. (In-process ALC plugins and container-per-plugin orchestration are later steps.)
/// </summary>
public sealed class PluginSupervisor : BackgroundService
{
    private readonly SupervisorOptions _options;
    private readonly ILogger<PluginSupervisor> _logger;
    private readonly ConcurrentDictionary<string, PluginEntry> _plugins = new();
    private readonly object _installLock = new();
    private HostResources _host = new(0, 0, false, false);
    private FileSystemWatcher? _watcher;

    public PluginSupervisor(IOptions<SupervisorOptions> options, ILogger<PluginSupervisor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public HostResources Host => _host;
    public IReadOnlyCollection<PluginEntry> Plugins => _plugins.Values.ToList();
    public PluginEntry? Get(string id) => _plugins.TryGetValue(id, out var e) ? e : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _host = HostResources.Detect(_options);
        _logger.LogInformation("Host resources: {Cores} cores, {Mem} MB, GPU={Gpu}, Internet={Net}",
            _host.CpuCores, _host.MemoryMb, _host.Gpu, _host.Internet);

        foreach (var entry in ManifestLoader.Discover(_options.PluginsRoot, _logger))
        {
            Classify(entry);
            _plugins[entry.Manifest.Id] = entry;
            if (entry.Status == PluginStatus.Discovered && entry.Manifest.AutoStart)
                Launch(entry);
        }

        StartWatching();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { Monitor(); ReconcileNewFolders(); }
            catch (Exception ex) { _logger.LogError(ex, "Plugin monitor tick failed"); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }

        _watcher?.Dispose();
        foreach (var e in _plugins.Values) KillProcess(e);
    }

    /// <summary>Decide the initial status from the operator switch + resource gating.</summary>
    private void Classify(PluginEntry entry)
    {
        if (!entry.Manifest.Enabled)
        {
            entry.Status = PluginStatus.Disabled;
            return;
        }

        var (ok, reason) = _host.CanSatisfy(entry.Manifest.Resources);
        if (!ok)
        {
            entry.Status = PluginStatus.Blocked;
            entry.Detail = reason;
            _logger.LogInformation("Plugin {Id} blocked: {Reason}", entry.Manifest.Id, reason);
            return;
        }

        entry.Status = PluginStatus.Discovered;
        entry.Detail = null;
    }

    /// <summary>Operator start. Honours resource gating; clears the operator-stop flag.</summary>
    public string Start(string id)
    {
        var entry = Get(id);
        if (entry is null) return "not found";
        if (entry.Status == PluginStatus.Disabled) return "plugin is disabled in its manifest";

        var (ok, reason) = _host.CanSatisfy(entry.Manifest.Resources);
        if (!ok) { entry.Status = PluginStatus.Blocked; entry.Detail = reason; return reason ?? "blocked"; }
        if (entry.IsAlive) return "already running";

        entry.StoppedByOperator = false;
        entry.RestartCount = 0;
        Launch(entry);
        return entry.Status == PluginStatus.Running ? "started" : (entry.Detail ?? "failed to start");
    }

    /// <summary>Operator stop. Suppresses auto-restart until started again.</summary>
    public string Stop(string id)
    {
        var entry = Get(id);
        if (entry is null) return "not found";
        entry.StoppedByOperator = true;
        entry.NextRestartAt = null;
        KillProcess(entry);
        entry.Status = PluginStatus.Stopped;
        return "stopped";
    }

    /// <summary>
    /// Installs a plugin from an uploaded <c>.zip</c> package (roadmap Epic 1C — UI install): stage + validate
    /// the archive, move it into the plugins root under its manifest id (replacing any previous version), then
    /// register it and auto-start it — no container access, no restart. This is the explicit "new plugin
    /// loaded" event; the file-watcher and periodic reconcile below cover packages dropped in out-of-band.
    /// </summary>
    public async Task<InstallResult> InstallAsync(Stream zip, CancellationToken ct)
    {
        var stagingRoot = Path.Combine(_options.PluginsRoot, ManifestLoader.StagingFolderName);
        Directory.CreateDirectory(stagingRoot);
        var stageId = Guid.NewGuid().ToString("N");
        var stageDir = Path.Combine(stagingRoot, stageId);

        // Copy the upload to disk first so the (synchronous) zip extraction doesn't block on the network stream.
        var tempZip = Path.Combine(stagingRoot, stageId + ".zip");
        try
        {
            await using (var fs = File.Create(tempZip)) await zip.CopyToAsync(fs, ct);

            PluginPackage.Staged staged;
            try
            {
                await using var read = File.OpenRead(tempZip);
                staged = PluginPackage.Stage(read, stagingRoot, stageId);
            }
            catch (InvalidPluginPackageException ex)
            {
                return new InstallResult(InstallOutcome.BadRequest, ex.Message, null);
            }

            lock (_installLock)
            {
                var id = staged.Manifest.Id;
                var target = Path.Combine(_options.PluginsRoot, id);

                if (_plugins.TryGetValue(id, out var existing))
                {
                    existing.StoppedByOperator = false;
                    KillProcess(existing); // release file handles before overwriting the folder
                }

                try
                {
                    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                    Directory.Move(staged.Folder, target);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to place plugin {Id}", id);
                    return new InstallResult(InstallOutcome.Error, $"could not place plugin: {ex.Message}", null);
                }

                var entry = RegisterFolder(target, force: true);
                _logger.LogInformation("Installed plugin {Id} from upload → {Status}", id, entry?.Status);
                return entry is null
                    ? new InstallResult(InstallOutcome.Error, "installed but manifest could not be re-read", null)
                    : new InstallResult(InstallOutcome.Installed, "installed", entry);
            }
        }
        finally
        {
            PluginPackage.TryDelete(stageDir);
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* best effort */ }
        }
    }

    /// <summary>Stop a plugin and delete its folder from the plugins root (UI uninstall).</summary>
    public string Uninstall(string id)
    {
        lock (_installLock)
        {
            var entry = Get(id);
            if (entry is null) return "not found";

            entry.StoppedByOperator = true;
            KillProcess(entry);
            _plugins.TryRemove(id, out _);

            try
            {
                if (Directory.Exists(entry.Folder)) Directory.Delete(entry.Folder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Removed plugin {Id} from the registry but could not delete its folder", id);
                return "unregistered; folder could not be deleted";
            }

            _logger.LogInformation("Uninstalled plugin {Id}", id);
            return "uninstalled";
        }
    }

    /// <summary>
    /// (Re)register the plugin in <paramref name="folder"/> from its manifest and start it if satisfiable.
    /// With <paramref name="force"/> a running plugin is replaced (used by install); without it, an already
    /// running plugin is left untouched (used by the watcher/reconcile so they never thrash a live plugin).
    /// </summary>
    private PluginEntry? RegisterFolder(string folder, bool force)
    {
        if (!ManifestLoader.TryLoad(folder, _logger, out var entry)) return null;
        var id = entry.Manifest.Id;

        if (_plugins.TryGetValue(id, out var existing))
        {
            if (existing.IsAlive && !force) return existing;
            KillProcess(existing);
        }

        Classify(entry);
        _plugins[id] = entry;
        if (entry.Status == PluginStatus.Discovered && entry.Manifest.AutoStart)
            Launch(entry);
        return entry;
    }

    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(_options.PluginsRoot);
            _watcher = new FileSystemWatcher(_options.PluginsRoot, "plugin.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += OnManifestChanged;
            _watcher.Changed += OnManifestChanged;
            _watcher.EnableRaisingEvents = true;
            _logger.LogInformation("Watching {Root} for new/updated plugins", _options.PluginsRoot);
        }
        catch (Exception ex)
        {
            // Some mounted filesystems don't support inotify — the 5s reconcile still picks new plugins up.
            _logger.LogWarning(ex, "Plugin file-watcher unavailable; falling back to periodic reconcile only");
        }
    }

    private void OnManifestChanged(object sender, FileSystemEventArgs e)
    {
        var folder = Path.GetDirectoryName(e.FullPath);
        if (folder is null || folder.Contains(ManifestLoader.StagingFolderName, StringComparison.Ordinal)) return;

        try
        {
            lock (_installLock)
            {
                var entry = RegisterFolder(folder, force: false);
                if (entry is not null)
                    _logger.LogInformation("Picked up plugin {Id} via watcher → {Status}", entry.Manifest.Id, entry.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle plugin change at {Folder}", folder);
        }
    }

    /// <summary>Safety net for filesystems where inotify is unreliable: register any new plugin folder.</summary>
    private void ReconcileNewFolders()
    {
        if (!Directory.Exists(_options.PluginsRoot)) return;

        foreach (var folder in Directory.EnumerateDirectories(_options.PluginsRoot))
        {
            if (string.Equals(Path.GetFileName(folder), ManifestLoader.StagingFolderName, StringComparison.Ordinal))
                continue;
            if (!File.Exists(Path.Combine(folder, "plugin.json"))) continue;
            if (_plugins.Values.Any(p => string.Equals(p.Folder, folder, StringComparison.Ordinal))) continue;

            lock (_installLock)
            {
                var entry = RegisterFolder(folder, force: false);
                if (entry is not null)
                    _logger.LogInformation("Reconciled new plugin {Id} → {Status}", entry.Manifest.Id, entry.Status);
            }
        }
    }

    private void Launch(PluginEntry entry)
    {
        var m = entry.Manifest;
        if (!string.Equals(m.Kind, "process", StringComparison.OrdinalIgnoreCase))
        {
            entry.Status = PluginStatus.Failed;
            entry.Detail = $"unsupported plugin kind '{m.Kind}' (only 'process')";
            return;
        }
        if (string.IsNullOrWhiteSpace(m.Command))
        {
            entry.Status = PluginStatus.Failed;
            entry.Detail = "manifest has no command";
            return;
        }

        try
        {
            entry.Status = PluginStatus.Starting;
            var psi = new ProcessStartInfo
            {
                FileName = m.Command,
                WorkingDirectory = entry.Folder,
                UseShellExecute = false,
            };
            foreach (var arg in m.Args) psi.ArgumentList.Add(arg);

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");

            entry.Process = process;
            entry.Status = PluginStatus.Running;
            entry.Detail = null;
            entry.NextRestartAt = null;
            entry.LastStartedAt = DateTime.UtcNow;
            _logger.LogInformation("Started plugin {Id} (pid {Pid})", m.Id, process.Id);
        }
        catch (Exception ex)
        {
            entry.Status = PluginStatus.Failed;
            entry.Detail = ex.Message;
            _logger.LogError(ex, "Failed to start plugin {Id}", m.Id);
        }
    }

    /// <summary>Detect exits, isolate failures, and restart crashed plugins with capped backoff.</summary>
    private void Monitor()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _plugins.Values)
        {
            // Pending restart due?
            if (entry.Status == PluginStatus.Starting && entry.Process is null
                && entry.NextRestartAt is { } due && now >= due)
            {
                Launch(entry);
                continue;
            }

            if (entry.Process is null) continue;
            if (!entry.Process.HasExited) continue;

            // The process exited.
            var exitCode = SafeExitCode(entry.Process);
            entry.Process.Dispose();
            entry.Process = null;
            entry.LastExitAt = now;

            if (entry.StoppedByOperator)
            {
                entry.Status = PluginStatus.Stopped;
                continue;
            }

            if (entry.Manifest.AutoStart && entry.RestartCount < _options.MaxRestarts)
            {
                entry.RestartCount++;
                var backoff = TimeSpan.FromSeconds(_options.RestartBackoffSeconds * entry.RestartCount);
                entry.NextRestartAt = now + backoff;
                entry.Status = PluginStatus.Starting;
                entry.Detail = $"exited (code {exitCode}); restart {entry.RestartCount}/{_options.MaxRestarts} in {backoff.TotalSeconds:0}s";
                _logger.LogWarning("Plugin {Id} {Detail}", entry.Manifest.Id, entry.Detail);
            }
            else
            {
                entry.Status = PluginStatus.Failed;
                entry.Detail = $"exited (code {exitCode}); giving up after {entry.RestartCount} restart(s)";
                _logger.LogError("Plugin {Id} {Detail}", entry.Manifest.Id, entry.Detail);
            }
        }
    }

    private void KillProcess(PluginEntry entry)
    {
        try
        {
            if (entry.Process is { HasExited: false } p)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
            entry.Process?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping plugin {Id}", entry.Manifest.Id);
        }
        finally
        {
            entry.Process = null;
        }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
