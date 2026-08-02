// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Notifications;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// The pure notification-discipline decision (roadmap Epic 3F): given a message, the enabled channels and the
/// live settings, decide <b>which channels</b> deliver it and whether it is <b>suppressed</b> as a duplicate.
/// Deliberately infrastructure-free so the whole discipline — per-type routing, the safety floor and the
/// rate-limit — is unit-testable deterministically (mirrors <c>LoadShedPlanner</c>/<c>PresenceState</c>).
///
/// <para>Order of decisions: (1) per-category mute filters the enabled channels; (2) the safety floor re-adds a
/// prominent channel for a critical message; (3) dedup drops a non-safety repeat inside the category's window.
/// Safety (critical) is never muted below "prominent" and never rate-limited — a safety alert must land.</para>
/// </summary>
public static class NotificationPolicy
{
    /// <summary>A channel as the policy sees it — its name (the mute key) and whether it's prominent.</summary>
    public sealed record ChannelInfo(string Name, bool Prominent);

    /// <summary>The policy's verdict for one message.</summary>
    /// <param name="Channels">Channel names to deliver on (empty ⇒ nothing delivered).</param>
    /// <param name="Suppressed">True when the message was dropped as a rate-limited duplicate.</param>
    /// <param name="Reason">Short reason code for logging: <c>ok</c>, <c>deduped</c>, <c>no_channels</c>,
    /// <c>safety_quiet_only</c> (delivered, but only quiet channels exist for a safety alert).</param>
    public sealed record Decision(IReadOnlyList<string> Channels, bool Suppressed, string Reason);

    /// <summary>
    /// Decide delivery for <paramref name="message"/>. <paramref name="lastSent"/> is the dispatcher-owned map of
    /// dedup-key → last delivery time; on a delivered (non-suppressed) decision this method records the send into
    /// it so the next duplicate can be caught. <paramref name="now"/> is injected for testability.
    /// </summary>
    public static Decision Decide(
        NotificationMessage message,
        IReadOnlyList<ChannelInfo> enabled,
        NotificationRuntimeState settings,
        DateTimeOffset now,
        IDictionary<string, DateTimeOffset> lastSent)
    {
        var category = NotificationCategories.Normalize(message.Category);
        var safety = NotificationSeverities.IsSafety(message.Severity);

        if (enabled.Count == 0)
            return new Decision(Array.Empty<string>(), Suppressed: false, "no_channels");

        // (1) Per-category mute (opt-out) — a safety message ignores mute (it may not be silenced).
        var chosen = safety
            ? enabled.ToList()
            : enabled.Where(c => !settings.IsMuted(category, c.Name)).ToList();

        var reason = "ok";

        // (2) Safety floor: a critical alert must reach a prominent channel. If the chosen set has none but a
        // prominent one is enabled, force the prominent channels in. If no prominent channel exists at all, we
        // deliver on what we have (quiet) but flag it so the dispatcher logs the gap.
        if (safety && settings.SafetyFloorEnabled && !chosen.Any(c => c.Prominent))
        {
            var prominent = enabled.Where(c => c.Prominent).ToList();
            if (prominent.Count > 0)
                chosen = chosen.Concat(prominent).DistinctBy(c => c.Name).ToList();
            else
                reason = "safety_quiet_only";
        }

        if (chosen.Count == 0)
            return new Decision(Array.Empty<string>(), Suppressed: false, "no_channels");

        // (3) Dedup / rate-limit — never for safety (a safety repeat must still land).
        var key = DedupKey(message, category);
        if (!safety)
        {
            var window = settings.MinIntervalSeconds(category);
            if (window > 0 && lastSent.TryGetValue(key, out var last)
                && (now - last).TotalSeconds < window)
            {
                return new Decision(Array.Empty<string>(), Suppressed: true, "deduped");
            }
        }

        lastSent[key] = now;
        return new Decision(chosen.Select(c => c.Name).ToList(), Suppressed: false, reason);
    }

    /// <summary>Stable identity for dedup: the caller's explicit key if any, else category + title + body.</summary>
    public static string DedupKey(NotificationMessage message, string category) =>
        !string.IsNullOrWhiteSpace(message.DedupKey)
            ? $"k:{message.DedupKey}"
            : $"{category}|{message.Title}|{message.Body}";
}
