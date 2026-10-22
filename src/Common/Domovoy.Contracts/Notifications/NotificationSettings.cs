// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Notifications;

/// <summary>
/// User-facing notification discipline settings (roadmap Epic 3F). A single persisted document the DbGateway owns
/// and the AutomationService's dispatcher reads (via the refresh loop, like the ML/load settings). It expresses
/// <b>per-type channel routing</b> (which categories go to which channels) as an <b>opt-out</b>: by default every
/// enabled channel gets every category, and the user mutes specific (category, channel) pairs — so a fresh install
/// is fully wired and a user narrows it, never widens into surprise. Plus per-category rate-limiting (against the
/// "cry wolf" fatigue NN/g warns about) and the safety floor.
///
/// <para><b>Safety is never suppressed.</b> A <c>critical</c> notification ignores the mute + rate-limit and, when
/// <see cref="SafetyFloorEnabled"/> is on, is forced onto a prominent channel — a safety alert must land.</para>
/// </summary>
public class NotificationSettings
{
    /// <summary>There is only ever one notification-settings document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// Per-category opt-out routing: category (<see cref="NotificationCategories"/>) → the channel names the user
    /// has muted for it. A channel absent from a category's list still receives it. Empty ⇒ every enabled channel
    /// receives every category (the default).
    /// </summary>
    public Dictionary<string, List<string>> MutedChannels { get; set; } = new();

    /// <summary>
    /// Per-category minimum seconds between <b>duplicate</b> notifications (dedup/rate-limit). A repeat with the
    /// same dedup key (or same title+body) within the window is dropped, killing the "humidity alert every 30 s"
    /// cry-wolf failure mode. A category missing here uses the dispatcher's built-in default. Safety (critical) is
    /// never rate-limited regardless of this.
    /// </summary>
    public Dictionary<string, int> MinIntervalSeconds { get; set; } = new();

    /// <summary>
    /// When true (default), a <c>critical</c> (safety) notification is always delivered on at least one prominent
    /// channel even if the user muted every prominent channel for its category — a safety alert must never go out
    /// only through a low-visibility channel (the in-app banner). Turn off only deliberately.
    /// </summary>
    public bool SafetyFloorEnabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
