// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Updater.Configuration;

/// <summary>
/// Runtime settings of the delivery service (roadmap Epic 3K).
/// <para>
/// Note what is <b>not</b> here: the registry, the package names, the update source. Those are
/// compiled-in constants in <c>ReleaseSource</c> and deliberately not configurable — the only thing
/// an operator picks is the channel, and that lives in the database next to the other settings.
/// </para>
/// </summary>
public sealed class UpdaterOptions
{
    public const string Section = "Updates";

    /// <summary>
    /// Installation root on the host, bind-mounted into this container: <c>releases/</c>,
    /// <c>state/</c>, <c>.env</c>, <c>docker-compose.override.yml</c>.
    /// </summary>
    public string InstallDirectory { get; set; } = "/opt/domovoy";

    /// <summary>Where backups are requested before applying an update.</summary>
    public string DbGatewayBaseUrl { get; set; } = "http://db-gateway:8080";

    public string DockerSocket { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>
    /// Periodic registry polling. Off in local development (see docker-compose.override.yml):
    /// nobody wants "a new version is available" while they are the one making the versions.
    /// </summary>
    public bool CheckEnabled { get; set; } = true;

    public int CheckIntervalHours { get; set; } = 6;

    /// <summary>Delay before the first check, so a cold start settles first.</summary>
    public int InitialCheckDelaySeconds { get; set; } = 120;

    /// <summary>How many versions per component to consider — room for the resolver to pick an older-but-compatible build.</summary>
    public int MaxVersionsPerComponent { get; set; } = 5;

    /// <summary>How long to wait for a recreated container to come up before calling the step failed.</summary>
    public int ContainerStartTimeoutSeconds { get; set; } = 120;

    // ---- производные пути установки ----

    public string ReleasesDirectory => Path.Combine(InstallDirectory, "releases");
    public string StateDirectory => Path.Combine(InstallDirectory, "state");
    public string CurrentReleaseLink => Path.Combine(InstallDirectory, "current");
    public string EnvFile => Path.Combine(InstallDirectory, ".env");
    public string OverrideFile => Path.Combine(InstallDirectory, "docker-compose.override.yml");
    public string PinnedFile => Path.Combine(StateDirectory, "pinned.yml");
    public string PreviousPinnedFile => Path.Combine(StateDirectory, "pinned.prev.yml");
    public string CurrentRunFile => Path.Combine(StateDirectory, "current-run.json");
    public string TopologyStateFile => Path.Combine(StateDirectory, "topology.json");
    public string HistoryFile => Path.Combine(StateDirectory, "history.json");
}
