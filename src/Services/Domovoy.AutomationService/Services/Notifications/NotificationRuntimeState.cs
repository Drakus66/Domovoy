// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Notifications;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Live copy of the notification-discipline settings (roadmap Epic 3F). <see cref="Services.RefreshLoop"/>
/// re-reads the <c>notification_settings</c> document each cycle and pushes it here, so muting a channel or
/// changing a rate-limit from the UI takes effect within one refresh without a restart. The dispatcher reads
/// this on every message. Defaults to "everything delivered, safety floor on", so a fresh install is fully wired
/// and a gateway outage never silently changes delivery (offline-first: keep the last-known settings).
/// </summary>
public sealed class NotificationRuntimeState
{
    // Built-in per-category dedup windows (seconds) when the settings don't override one. Reactive is short (it
    // still kills the "same alert every 30 s" cry-wolf loop); proactive/optimization are calmer.
    private static readonly IReadOnlyDictionary<string, int> DefaultIntervals = new Dictionary<string, int>
    {
        [NotificationCategories.Reactive] = 60,
        [NotificationCategories.Proactive] = 900,
        [NotificationCategories.Optimization] = 3600,
    };

    private volatile NotificationSettings _settings = new();

    /// <summary>The last-known settings (never null).</summary>
    public NotificationSettings Current => _settings;

    /// <summary>Whether a safety (critical) message is forced onto a prominent channel despite mutes.</summary>
    public bool SafetyFloorEnabled => _settings.SafetyFloorEnabled;

    /// <summary>Replace the live settings (called by the refresh loop). A null is ignored — keep last-known.</summary>
    public void Set(NotificationSettings? settings)
    {
        if (settings is not null) _settings = settings;
    }

    /// <summary>Whether the user muted <paramref name="channel"/> for <paramref name="category"/>.</summary>
    public bool IsMuted(string category, string channel)
    {
        var s = _settings;
        return s.MutedChannels.TryGetValue(category, out var muted)
            && muted.Any(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The dedup window for a category — the setting if present and non-negative, else the built-in default.</summary>
    public int MinIntervalSeconds(string category)
    {
        var s = _settings;
        if (s.MinIntervalSeconds.TryGetValue(category, out var v) && v >= 0) return v;
        return DefaultIntervals.TryGetValue(category, out var d) ? d : 0;
    }
}
