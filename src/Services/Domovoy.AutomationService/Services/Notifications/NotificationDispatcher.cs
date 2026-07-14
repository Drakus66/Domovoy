// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Fans a notification out to every enabled delivery channel (roadmap Epic 2G). Channel failures are
/// isolated — one channel erroring never blocks the others or the caller (rule execution). When no channel
/// is enabled the message is logged only, so the box stays fully functional offline (principle 2).
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IEnumerable<INotificationChannel> channels, ILogger<NotificationDispatcher> logger)
    {
        _channels = channels.ToList();
        _logger = logger;
    }

    /// <summary>Names of the currently enabled channels (for the status endpoint / UI).</summary>
    public IReadOnlyList<string> EnabledChannels =>
        _channels.Where(c => c.Enabled).Select(c => c.Name).ToList();

    /// <summary>Names of all registered channels regardless of enablement.</summary>
    public IReadOnlyList<string> AllChannels => _channels.Select(c => c.Name).ToList();

    /// <summary>Deliver a message to all enabled channels; returns how many accepted it.</summary>
    public async Task<int> DispatchAsync(NotificationMessage message, CancellationToken ct)
    {
        var enabled = _channels.Where(c => c.Enabled).ToList();
        if (enabled.Count == 0)
        {
            _logger.LogInformation("Notification (no channels): {Title} — {Body}", message.Title, message.Body);
            return 0;
        }

        var results = await Task.WhenAll(enabled.Select(async c =>
        {
            try { return await c.SendAsync(message, ct) ? 1 : 0; }
            catch (Exception ex) { _logger.LogWarning(ex, "Channel {Channel} threw", c.Name); return 0; }
        }));

        return results.Sum();
    }
}
