// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Ml;
using Domovoy.Contracts.Notifications;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// The shared "silence and pulse" policy for the three proactive proposers (roadmap Epic 3I): the rule
/// heuristic (2C), the ML-task suggester (2P) and the pattern-discovery engine (2F). One place decides whether
/// a periodic scan may run (layer + proposers enabled, and the history is old enough to be worth mining), writes
/// the ML-activity journal entry that makes the layer's work visible on the ML page, and raises the single
/// "found something" notification. Manual "scan now" endpoints bypass the gate — an explicit human action is
/// never spam — but still journal their outcome.
/// </summary>
public sealed class MlProposerGate
{
    private readonly DbGatewayClient _db;
    private readonly MlRuntimeState _runtime;
    private readonly NotificationDispatcher _notifications;

    public MlProposerGate(DbGatewayClient db, MlRuntimeState runtime, NotificationDispatcher notifications)
    {
        _db = db;
        _runtime = runtime;
        _notifications = notifications;
    }

    /// <summary>Whether a periodic scan may run now, and why not otherwise.</summary>
    /// <param name="Journal">True only for the states worth recording as progress (history still accruing) —
    /// the persistent "off" states are already obvious from the settings banner, so they are not journalled
    /// every cycle.</param>
    public sealed record GateDecision(bool Allowed, string Reason, double HistoryDays, double RequiredDays, bool Journal);

    /// <summary>
    /// Evaluate the gate: master switch, proposers switch, then the cold-start history gate. A gateway that is
    /// unreachable (or an empty log) reads as "not mature yet" but is not journalled as progress (we cannot tell
    /// an empty log from an outage).
    /// </summary>
    public async Task<GateDecision> EvaluateAsync(CancellationToken ct)
    {
        if (!_runtime.LayerEnabled) return new GateDecision(false, "layer_disabled", 0, 0, Journal: false);
        if (!_runtime.ProposalsEnabled) return new GateDecision(false, "proposals_disabled", 0, 0, Journal: false);

        var required = _runtime.MinHistoryDays;
        if (required <= 0) return new GateDecision(true, "ok", 0, 0, Journal: false);

        var earliest = await _db.GetEarliestEventAsync(ct);
        if (earliest is not { } e0) return new GateDecision(false, "history_immature", 0, required, Journal: false);

        var historyDays = Math.Max(0, (DateTime.UtcNow - e0).TotalDays);
        return historyDays < required
            ? new GateDecision(false, "history_immature", Math.Round(historyDays, 1), required, Journal: true)
            : new GateDecision(true, "ok", Math.Round(historyDays, 1), required, Journal: false);
    }

    /// <summary>Record a gated-off cycle in the journal (only when the decision says it is worth recording).</summary>
    public Task WriteSkippedAsync(string source, GateDecision d, CancellationToken ct)
    {
        if (!d.Journal) return Task.CompletedTask;
        return _db.WriteMlActivityAsync(new MlActivityEntry
        {
            Source = source,
            Outcome = MlActivityOutcomes.Skipped,
            Reason = d.Reason,
            Note = $"accumulating history: {d.HistoryDays:0.#} of {d.RequiredDays:0} day(s)",
            Metrics = new Dictionary<string, double> { ["historyDays"] = d.HistoryDays, ["requiredDays"] = d.RequiredDays },
        }, ct);
    }

    /// <summary>Record the outcome of a completed scan (ok when it created something, idle otherwise).</summary>
    public Task WriteScanAsync(
        string source, int candidates, int created, string note, Dictionary<string, double>? extraMetrics, CancellationToken ct)
    {
        var metrics = extraMetrics ?? new Dictionary<string, double>();
        metrics["candidates"] = candidates;
        metrics["created"] = created;
        var outcome = created > 0 ? MlActivityOutcomes.Ok : MlActivityOutcomes.Idle;
        var reason = created > 0 ? "created" : candidates > 0 ? "all_known" : "no_patterns";
        return _db.WriteMlActivityAsync(new MlActivityEntry
        {
            Source = source, Outcome = outcome, Reason = reason, Note = note, Metrics = metrics,
        }, ct);
    }

    /// <summary>Record a failed cycle (gateway unreachable, exception).</summary>
    public Task WriteErrorAsync(string source, string note, CancellationToken ct) =>
        _db.WriteMlActivityAsync(new MlActivityEntry
        {
            Source = source, Outcome = MlActivityOutcomes.Error, Reason = "error", Note = note,
        }, ct);

    /// <summary>
    /// Raise the single aggregated "found something" notification for a scan that queued ≥1 proposal (Epic 3I).
    /// Info severity — the always-visible signal is the nav badge; this is the gentle, non-banner poke that also
    /// reaches a push channel if one is configured. No-op when nothing was created.
    /// </summary>
    public async Task NotifyFindingsAsync(int created, CancellationToken ct)
    {
        if (created <= 0) return;
        var body = created == 1
            ? "Found a new pattern — review it in Proposals."
            : $"Found {created} new patterns — review them in Proposals.";
        // Proactive (Epic 3F taxonomy): the always-on signal is the nav badge; this is the gentle poke that also
        // reaches a push channel if one is configured, with a one-tap "open" action to the proposals queue.
        var open = new NotificationAction("open", "Открыть предложения", NotificationActionKinds.Open,
            new Dictionary<string, string> { ["route"] = "/proposals" });
        await _notifications.DispatchAsync(
            new NotificationMessage("Domovoy", body, NotificationSeverities.Info, NotificationCategories.Proactive,
                Actions: new[] { open }, DedupKey: "discovery-findings"),
            ct);
    }
}
