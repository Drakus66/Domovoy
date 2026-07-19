// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text;

using Domovoy.AutomationService.Configuration;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Off-LAN push channel via ntfy / UnifiedPush (Epic 2O.4). POSTs the message to <c>{ServerUrl}/{Topic}</c> — the
/// body is the message text, and Title / Priority / Tags travel as headers (ntfy's simplest publishing API). A
/// self-hosted ntfy server + the UnifiedPush distributor in the app deliver it to a phone that is asleep or
/// outside the LAN, where the SignalR banner (2M.2) can't reach. Off by default (external dependency); ntfy runs
/// as a separate self-hosted server (its own license), we only make HTTP calls to it.
/// </summary>
public sealed class NtfyChannel : INotificationChannel
{
    private readonly IHttpClientFactory _http;
    private readonly NtfyChannelOptions _options;
    private readonly ILogger<NtfyChannel> _logger;

    public NtfyChannel(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<NtfyChannel> logger)
    {
        _http = http;
        _options = options.Value.Ntfy;
        _logger = logger;
    }

    public string Name => "ntfy";

    public bool Enabled =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.ServerUrl)
        && !string.IsNullOrWhiteSpace(_options.Topic);

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(nameof(NtfyChannel));
            using var request = BuildRequest(message);

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ntfy delivery failed: {Status}", response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ntfy delivery error");
            return false;
        }
    }

    /// <summary>Build the ntfy publish request. Internal so a unit test can assert the URL/headers without a server.</summary>
    internal HttpRequestMessage BuildRequest(NotificationMessage message)
    {
        var url = $"{_options.ServerUrl.TrimEnd('/')}/{_options.Topic}";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            // The message body is the notification text; ntfy reads Title/Priority/Tags from headers.
            Content = new StringContent(message.Body, Encoding.UTF8),
        };
        request.Headers.TryAddWithoutValidation("Title", message.Title);
        request.Headers.TryAddWithoutValidation("Priority", PriorityFor(message.Severity));
        request.Headers.TryAddWithoutValidation("Tags", TagFor(message.Severity));
        if (!string.IsNullOrWhiteSpace(_options.Token))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.Token}");
        return request;
    }

    // ntfy priority is 1 (min) … 5 (max/urgent). Map severity so a critical alert can override Do-Not-Disturb.
    private static string PriorityFor(string severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => "5",
        "warning" => "4",
        _ => "3",
    };

    // ntfy renders tags as emoji when they match a known keyword.
    private static string TagFor(string severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => "rotating_light",
        "warning" => "warning",
        _ => "information_source",
    };
}
