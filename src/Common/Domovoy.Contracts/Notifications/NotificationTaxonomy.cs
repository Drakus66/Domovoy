// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Notifications;

/// <summary>
/// Notification taxonomy (roadmap Epic 3F — notification discipline). NN/g's three-way split of what a smart
/// home tells the user: <b>reactive</b> (something happened that needs attention — a rule fired, a load was
/// shed), <b>proactive</b> (the system found or suggests something — a discovered pattern, an ML proposal) and
/// <b>optimization</b> (a low-urgency efficiency nudge — "run the boiler in the cheap hours"). The category is
/// orthogonal to <see cref="NotificationSeverities">severity</see>: it drives per-type channel routing and
/// rate-limiting, so a user can, say, mute optimization on push but keep reactive everywhere.
/// </summary>
public static class NotificationCategories
{
    public const string Reactive = "reactive";
    public const string Proactive = "proactive";
    public const string Optimization = "optimization";

    public static readonly IReadOnlyList<string> All = new[] { Reactive, Proactive, Optimization };

    public static bool IsKnown(string? category) =>
        category is not null && All.Contains(category);

    /// <summary>Fallback for a missing/unknown category — reactive is the safest (least-suppressed) bucket.</summary>
    public static string Normalize(string? category) =>
        IsKnown(category) ? category! : Reactive;
}

/// <summary>
/// Notification severities (roadmap Epic 3F). A free triage level used for formatting and the banner threshold;
/// <c>critical</c> is the <b>safety class</b> — it must never be delivered only through a low-visibility ("quiet")
/// channel and is never rate-limited away (principle: a safety alert must land).
/// </summary>
public static class NotificationSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";

    /// <summary>Whether the severity marks a safety-critical message (the routing floor + no-rate-limit rule apply).</summary>
    public static bool IsSafety(string? severity) =>
        string.Equals(severity, Critical, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The action kinds an <see cref="NotificationAction"/> can carry (roadmap Epic 3F — actionable notifications).
/// Server-side kinds execute a command <b>with actor attribution</b> (the household member who tapped the button,
/// via the same actor-string as a manual command); client-only kinds are handled in the browser.
/// </summary>
public static class NotificationActionKinds
{
    /// <summary>Approve a queued proposal (Epic 2C) — params: <c>proposalId</c>. Server-side, attributed.</summary>
    public const string ApproveProposal = "approve_proposal";

    /// <summary>Reject a queued proposal (Epic 2C) — params: <c>proposalId</c>. Server-side, attributed.</summary>
    public const string RejectProposal = "reject_proposal";

    /// <summary>Send a device command — params: <c>deviceId</c>, <c>capabilityId</c>, <c>value</c>. Server-side, attributed.</summary>
    public const string DeviceCommand = "device_command";

    /// <summary>Switch the home mode (Epic 1G) — params: <c>mode</c>. Server-side, attributed.</summary>
    public const string SetMode = "set_mode";

    /// <summary>Navigate the WebUI to a route — params: <c>route</c>. Client-only (no bus command).</summary>
    public const string Open = "open";

    /// <summary>The set the action-executing endpoint handles server-side (the rest are client-only).</summary>
    public static readonly IReadOnlyList<string> ServerSide = new[]
    {
        ApproveProposal, RejectProposal, DeviceCommand, SetMode,
    };

    public static bool IsServerSide(string? kind) =>
        kind is not null && ServerSide.Contains(kind);
}

/// <summary>
/// One actionable button on a notification (roadmap Epic 3F). Rendered as a button on the banner/push; tapping a
/// server-side kind (<see cref="NotificationActionKinds"/>) posts to the action endpoint, which executes the
/// command with the tapping user's attribution. Deliberately generic (kind + string params) so new actions need
/// no contract change — the endpoint validates the kind and required params.
/// </summary>
public sealed record NotificationAction(
    string Id,
    string Label,
    string Kind,
    IReadOnlyDictionary<string, string>? Params = null);
