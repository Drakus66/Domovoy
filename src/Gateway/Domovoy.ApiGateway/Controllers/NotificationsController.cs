// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.ApiGateway.Services;
using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Notifications;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Notification surface. Proxies the delivery channels + test message to the AutomationService (which hosts the
/// channels + dispatcher, roadmap Epic 2G), and executes an <b>actionable</b> notification's button (roadmap Epic
/// 3F): approve/reject a proposal (2C), switch mode (1G) or send a device command — <b>with actor attribution</b>
/// (the household member who tapped it, via the same actor-string as a manual command). The action → execution
/// mapping is the pure <see cref="NotificationActionRouter"/>; this only carries it out.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ProxyController
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(IHttpClientFactory httpClientFactory, IMessageBus messageBus, ILogger<NotificationsController> logger)
        : base(httpClientFactory)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    [HttpGet("channels")]
    public Task<IActionResult> Channels(CancellationToken ct)
        => ForwardTo("automation-service", "api/notifications/channels", ct);

    [HttpPost("test")]
    public Task<IActionResult> Test(CancellationToken ct)
        => ForwardTo("automation-service", "api/notifications/test", ct);

    /// <summary>Execute an actionable-notification button. Body: a <see cref="NotificationAction"/>. The action's
    /// command is attributed to the tapping user (JWT <c>sub</c> / self-declared header), like a manual command.</summary>
    [HttpPost("action")]
    [Authorize]
    public async Task<IActionResult> ExecuteAction([FromBody] NotificationAction action, CancellationToken ct)
    {
        if (action is null || string.IsNullOrWhiteSpace(action.Kind))
            return BadRequest(new { error = "an action with a kind is required" });

        var actor = CommandSource();
        var plan = NotificationActionRouter.Resolve(action, actor);
        if (plan.Error is not null)
            return BadRequest(new { error = plan.Error });

        if (plan.IsDeviceCommand)
        {
            var envelope = Envelope<DeviceCommandV1>.Create(
                MessageTypes.DeviceCommand,
                source: actor,
                data: new DeviceCommandV1(plan.DeviceId, new Dictionary<string, object?> { [plan.CapabilityId!] = plan.Value }),
                subject: plan.DeviceId.ToString());
            await _messageBus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope, ct);
            _logger.LogInformation("Notification action {Kind} → device {DeviceId}.{Cap} by {Actor}",
                plan.Kind, plan.DeviceId, plan.CapabilityId, actor);
            return Accepted(new { plan.Kind, deviceId = plan.DeviceId });
        }

        // HTTP forward (approve/reject proposal, set mode).
        var client = HttpClientFactory.CreateClient(plan.HttpClient!);
        using var request = new HttpRequestMessage(new HttpMethod(plan.HttpMethod!), plan.HttpPath!);
        if (plan.HttpJsonBody is not null)
            request.Content = new StringContent(plan.HttpJsonBody, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Notification action {Kind} → {Path} ({Status}) by {Actor}",
            plan.Kind, plan.HttpPath, (int)response.StatusCode, actor);

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = string.IsNullOrEmpty(responseBody) ? null : responseBody,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }

    /// <summary>Actor-string for attribution — JWT subject, else the self-declared local user header, else anonymous.
    /// Same rule as <see cref="DeviceControlController"/> so a button-tap is attributed like a manual command.</summary>
    private string CommandSource()
    {
        var userId = User.FindFirst("sub")?.Value
                     ?? Request.Headers["X-Domovoy-User"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(userId) || userId.Length > 64 || userId.Contains(':')) return "apigateway";
        return $"user:{userId}";
    }
}
