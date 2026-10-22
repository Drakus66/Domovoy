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

    /// <summary>First-party LAN channel over SignalR (2M.2) — the in-app banner. On by default: it's local,
    /// has no external dependency and no privacy concern, and is the primary notification surface.</summary>
    public LanChannelOptions Lan { get; set; } = new();

    /// <summary>Self-hosted push for off-LAN delivery via ntfy / UnifiedPush (Epic 2O.4). Off by default.</summary>
    public NtfyChannelOptions Ntfy { get; set; } = new();

    public TelegramChannelOptions Telegram { get; set; } = new();
    public WebhookChannelOptions Webhook { get; set; } = new();
}

/// <summary>LAN SignalR channel (2M.2): publishes NotificationRaisedV1 on the bus for the ApiGateway to relay
/// to connected clients. On by default — the banner is the point; set Enabled=false to silence it.</summary>
public sealed class LanChannelOptions
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// ntfy push channel (Epic 2O.4) — POSTs to a self-hosted ntfy server so an asleep / off-LAN phone still gets
/// the notification via UnifiedPush. Off by default (external dependency). Publishing to <c>{ServerUrl}/{Topic}</c>
/// with the message as the body and Title/Priority/Tags as headers is ntfy's simplest API.
/// </summary>
public sealed class NtfyChannelOptions
{
    public bool Enabled { get; set; }

    /// <summary>Base URL of the ntfy server, e.g. <c>https://ntfy.example.com</c> (self-hosted preferred over ntfy.sh).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>The topic the app subscribes to. Treat it as a shared secret — anyone who knows it can publish.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Optional access token for a protected ntfy server (sent as <c>Authorization: Bearer …</c>).</summary>
    public string? Token { get; set; }
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
