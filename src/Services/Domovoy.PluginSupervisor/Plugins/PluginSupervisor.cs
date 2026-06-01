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
    private HostResources _host = new(0, 0, false, false);

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

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { Monitor(); }
            catch (Exception ex) { _logger.LogError(ex, "Plugin monitor tick failed"); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }

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
