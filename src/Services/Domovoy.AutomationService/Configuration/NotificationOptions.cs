// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Configuration;

/// <summary>
/// Notification delivery configuration (roadmap Epic 2G — delivery channels). Provider-agnostic and
/// <b>env-gated, off by default</b>: with no channel enabled the system logs the notification and nothing
/// leaves the box, preserving the offline-first invariant (principle 2). Channels are optional external
/// integrations — a channel failing (or the internet being down) never blocks rule execution.
/// Bound from the <c>Notifications</c> config section (e.g. <c>Notifications__Telegram__Enabled=true</c>).
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public TelegramChannelOptions Telegram { get; set; } = new();
    public WebhookChannelOptions Webhook { get; set; } = new();
}

/// <summary>Telegram Bot API channel — sends to a chat via a bot token.</summary>
public sealed class TelegramChannelOptions
{
    public bool Enabled { get; set; }

    /// <summary>Bot token from @BotFather.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Target chat id (user, group or channel).</summary>
    public string ChatId { get; set; } = string.Empty;
}

/// <summary>
/// Generic webhook / push channel — POSTs a JSON body <c>{ title, body, severity, timestamp }</c> to a URL.
/// Covers self-hosted push (ntfy, Gotify), chat webhooks (Discord/Slack-style) and custom endpoints.
/// </summary>
public sealed class WebhookChannelOptions
{
    public bool Enabled { get; set; }

    /// <summary>Endpoint the notification JSON is POSTed to.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional <c>Authorization</c> header value (e.g. <c>Bearer …</c>).</summary>
    public string? AuthHeader { get; set; }
}
