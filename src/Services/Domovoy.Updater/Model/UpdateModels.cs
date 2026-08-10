// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Updater.Model;

/// <summary>Release channel the installation is subscribed to.</summary>
public static class UpdateChannels
{
    public const string Dev = "dev";
    public const string Release = "release";

    public static bool IsKnown(string? channel) =>
        channel is Dev or Release;

    public static string Normalize(string? channel) =>
        IsKnown(channel) ? channel! : Release;
}

/// <summary>The pseudo-interface the topology bundle provides; a component requires it like any other.</summary>
public static class WellKnownInterfaces
{
    public const string Topology = "topology";
}

/// <summary>A component as it is actually running on this host.</summary>
public sealed record InstalledComponent(
    string Name,
    string Container,
    string Repository,
    string Tag,
    string Digest,
    string Version,
    ComponentDeps Deps);

/// <summary>A component version offered by the registry on the subscribed channel.</summary>
public sealed record AvailableComponent(
    string Name,
    string Repository,
    string Tag,
    string Digest,
    string Version,
    ComponentDeps Deps);

/// <summary>
/// One component's move in a plan. <see cref="Reason"/> explains why it is in the plan at all —
/// the UI shows it verbatim ("ML requires db-api >= 5"), because "why is this being updated too"
/// is the first question anyone asks.
/// </summary>
public sealed record PlannedUpdate(
    string Component,
    string Container,
    string FromVersion,
    string ToVersion,
    string Repository,
    string Digest,
    string Reason);

/// <summary>
/// A set of components that move together. <see cref="Atomic"/> means the intermediate state is
/// incompatible (a provider raised <c>minCompat</c> past a live consumer, or two components require
/// each other), so they are stopped and recreated in one shot instead of one at a time.
/// <para>
/// This is the answer to dependency cycles: a cycle is not a deadlock, it is a demand for atomicity.
/// </para>
/// </summary>
public sealed record UpdateGroup(IReadOnlyList<PlannedUpdate> Members, bool Atomic);

/// <summary>
/// The outcome of resolution. Either an ordered list of groups, or a refusal with the conflicting
/// pair named — never a vague "cannot update".
/// </summary>
public sealed record UpdatePlan
{
    public IReadOnlyList<UpdateGroup> Groups { get; init; } = Array.Empty<UpdateGroup>();

    /// <summary>Null when resolution succeeded.</summary>
    public string? Refusal { get; init; }

    /// <summary>Components involved in the refusal, for a precise message in the UI.</summary>
    public IReadOnlyList<string> Conflict { get; init; } = Array.Empty<string>();

    public bool Ok => Refusal is null;

    public bool IsEmpty => Ok && Groups.Count == 0;

    /// <summary>Flattened plan in execution order — handy for the UI and the journal.</summary>
    public IReadOnlyList<PlannedUpdate> All => Groups.SelectMany(g => g.Members).ToList();

    public static UpdatePlan Empty() => new();

    public static UpdatePlan Refuse(string reason, params string[] conflict) =>
        new() { Refusal = reason, Conflict = conflict };
}

/// <summary>Live progress of an update run, persisted so it survives the UI restarting mid-flight.</summary>
public sealed record UpdateRunState
{
    public string Id { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary><c>running</c> | <c>ok</c> | <c>error</c> | <c>rolled-back</c>.</summary>
    public string Status { get; init; } = "running";

    public string Channel { get; init; } = UpdateChannels.Release;
    public string? Error { get; init; }

    /// <summary>Human-readable step log; the UI renders it as-is.</summary>
    public List<UpdateStep> Steps { get; init; } = new();

    /// <summary>What this run set out to do — used by rollback to know what to undo.</summary>
    public List<PlannedUpdate> Plan { get; init; } = new();

    /// <summary>Versions in place before the run, so a rollback has a target.</summary>
    public Dictionary<string, string> Previous { get; init; } = new();

    public string? BackupFile { get; init; }
    public int? TopologyFrom { get; init; }
    public int? TopologyTo { get; init; }

    /// <summary>Keys added to the host <c>.env</c> from the template, shown to the owner.</summary>
    public List<string> EnvKeysAdded { get; init; } = new();
}

public sealed record UpdateStep(
    string Name,
    string Status, // pending | running | ok | error | skipped
    DateTimeOffset At,
    string? Detail = null);
