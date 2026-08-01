// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Fans a notification out to the delivery channels chosen by the Epic 3F discipline policy
/// (<see cref="NotificationPolicy"/>): per-category channel routing, the safety floor, and rate-limit/dedup
/// against "cry wolf" fatigue. Channel failures are isolated — one channel erroring never blocks the others or
/// the caller (rule execution). When no channel is enabled (or the message is deduped) it is logged only, so the
/// box stays fully functional offline (principle 2). Epic 2G established the fan-out; 3F added the policy.
/// </summary>
public sealed class NotificationDispatcher
{
    // A dedup entry older than this is pruned — well past the longest built-in window, so pruning never drops a
    // key that could still suppress a duplicate.
    private static readonly TimeSpan DedupRetention = TimeSpan.FromHours(6);

    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly NotificationRuntimeState _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<NotificationDispatcher> _logger;

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);

    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        NotificationRuntimeState settings,
        ILogger<NotificationDispatcher> logger)
        : this(channels, settings, logger, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Test seam: inject the clock so dedup windows are deterministic.</summary>
    internal NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        NotificationRuntimeState settings,
        ILogger<NotificationDispatcher> logger,
        Func<DateTimeOffset> clock)
    {
        _channels = channels.ToList();
        _settings = settings;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>Names of the currently enabled channels (for the status endpoint / UI).</summary>
    public IReadOnlyList<string> EnabledChannels =>
        _channels.Where(c => c.Enabled).Select(c => c.Name).ToList();

    /// <summary>Names of all registered channels regardless of enablement.</summary>
    public IReadOnlyList<string> AllChannels => _channels.Select(c => c.Name).ToList();

    /// <summary>Deliver a message to the policy-chosen channels; returns how many accepted it (0 if deduped/none).</summary>
    public async Task<int> DispatchAsync(NotificationMessage message, CancellationToken ct)
    {
        var enabled = _channels.Where(c => c.Enabled).ToList();
        var infos = enabled
            .Select(c => new NotificationPolicy.ChannelInfo(c.Name, c.Visibility == NotificationVisibility.Prominent))
            .ToList();

        NotificationPolicy.Decision decision;
        lock (_gate)
        {
            var now = _clock();
            Prune(now);
            decision = NotificationPolicy.Decide(message, infos, _settings, now, _lastSent);
        }

        if (decision.Suppressed)
        {
            _logger.LogDebug("Notification deduped ({Category}): {Title}", message.Category, message.Title);
            return 0;
        }

        if (decision.Reason == "safety_quiet_only")
            _logger.LogWarning(
                "SAFETY notification has only quiet channels — it may go unseen with no app open: {Title}", message.Title);

        if (decision.Channels.Count == 0)
        {
            _logger.LogInformation("Notification (no channels): {Title} — {Body}", message.Title, message.Body);
            return 0;
        }

        var targets = enabled.Where(c => decision.Channels.Contains(c.Name)).ToList();
        var results = await Task.WhenAll(targets.Select(async c =>
        {
            try { return await c.SendAsync(message, ct) ? 1 : 0; }
            catch (Exception ex) { _logger.LogWarning(ex, "Channel {Channel} threw", c.Name); return 0; }
        }));

        return results.Sum();
    }

    private void Prune(DateTimeOffset now)
    {
        if (_lastSent.Count < 256) return; // cheap: only sweep once the map has grown
        var cutoff = now - DedupRetention;
        foreach (var key in _lastSent.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
            _lastSent.Remove(key);
    }
}
