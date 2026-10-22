// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;

using Domovoy.AutomationService.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Generic webhook / push delivery channel (roadmap Epic 2G). POSTs the notification as JSON to a
/// configured URL — covers self-hosted push (ntfy, Gotify), chat webhooks and custom endpoints. Enabled
/// only when a URL is configured.
/// </summary>
public sealed class WebhookChannel : INotificationChannel
{
    private readonly IHttpClientFactory _http;
    private readonly WebhookChannelOptions _options;
    private readonly ILogger<WebhookChannel> _logger;

    public WebhookChannel(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<WebhookChannel> logger)
    {
        _http = http;
        _options = options.Value.Webhook;
        _logger = logger;
    }

    public string Name => "webhook";

    public NotificationVisibility Visibility => NotificationVisibility.Prominent;

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Url);

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(nameof(WebhookChannel));
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
            {
                Content = JsonContent.Create(new
                {
                    title = message.Title,
                    body = message.Body,
                    severity = message.Severity,
                    timestamp = DateTimeOffset.UtcNow,
                }),
            };
            if (!string.IsNullOrWhiteSpace(_options.AuthHeader))
                request.Headers.TryAddWithoutValidation("Authorization", _options.AuthHeader);

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook delivery failed: {Status}", response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook delivery error");
            return false;
        }
    }
}
