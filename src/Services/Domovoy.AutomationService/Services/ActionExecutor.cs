// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Runs a fired rule's actions and records the outcome. Commands are published as
/// <see cref="DeviceCommandV1"/> with source <c>automation:{ruleId}</c> and the rule id as the
/// correlation id, so the P0-5 event-log attributes the resulting state change to a rule
/// (<c>triggerSource=rule</c>) — the hook for explainability (Epic 1F). A <see cref="AutomationTriggeredV1"/>
/// is always emitted (even when conditions failed) so run history is complete.
/// </summary>
public sealed class ActionExecutor
{
    private readonly IMessageBus _bus;
    private readonly NotificationDispatcher _notifications;
    private readonly TimeSpan _boundedCooldown;
    private readonly ILogger<ActionExecutor> _logger;

    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(24);

    /// <summary>Per-rule last real execution — the throttle clock for BoundedActive rules (Epic 1F).</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFired = new();

    public ActionExecutor(
        IMessageBus bus, NotificationDispatcher notifications,
        IOptions<AutomationOptions> options, ILogger<ActionExecutor> logger)
    {
        _bus = bus;
        _notifications = notifications;
        _boundedCooldown = TimeSpan.FromSeconds(Math.Max(0, options.Value.BoundedActiveCooldownSeconds));
        _logger = logger;
    }

    public async Task ExecuteAsync(AutomationRule rule, string triggerSummary, bool conditionsMet, CancellationToken ct)
    {
        var executed = 0;
        var success = true;
        string? detail = null;

        // Shadow rules (roadmap Epic 1F staged rollout): evaluate and log what they WOULD do, but publish
        // no commands and skip real delays — the "shadow mode" stage before a rule (or ML proposal) goes live.
        var shadow = rule.Status == RuleStatus.Shadow;

        // BoundedActive rules (Epic 1F, the stage between Shadow and Active): execute, but no more often than
        // the cooldown — bounding actuation rate (blast radius) while trust is still building. Protected/Active
        // safety rules are never throttled.
        if (conditionsMet && !shadow && rule.Status == RuleStatus.BoundedActive && !rule.IsProtected)
        {
            var last = _lastFired.TryGetValue(rule.Id, out var t) ? t : DateTimeOffset.MinValue;
            var elapsed = DateTimeOffset.UtcNow - last;
            if (elapsed < _boundedCooldown)
            {
                var wait = _boundedCooldown - elapsed;
                _logger.LogInformation("[{Rule}] BOUNDED throttled — {Wait:F0}s until next run", rule.Name, wait.TotalSeconds);
                await PublishHistory(rule, triggerSummary, conditionsMet, success: true, executed: 0,
                    detail: $"bounded: throttled ({wait.TotalSeconds:F0}s until next run)");
                return;
            }
            _lastFired[rule.Id] = DateTimeOffset.UtcNow;
        }

        if (conditionsMet)
        {
            try
            {
                var wouldRun = 0;
                foreach (var action in rule.Actions)
                {
                    switch (action.Type)
                    {
                        case ActionType.Command:
                            if (Guid.TryParse(action.DeviceId, out var deviceId) && action.Set is { Count: > 0 })
                            {
                                if (shadow)
                                {
                                    _logger.LogInformation("[{Rule}] SHADOW would set {Device} ← {Set}",
                                        rule.Name, action.DeviceId, string.Join(", ", action.Set.Keys));
                                    wouldRun++;
                                }
                                else
                                {
                                    var envelope = Envelope<DeviceCommandV1>.Create(
                                        MessageTypes.DeviceCommand,
                                        source: $"automation:{rule.Id}",
                                        data: new DeviceCommandV1(deviceId, action.Set),
                                        subject: action.DeviceId,
                                        correlationId: rule.Id);
                                    await _bus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope, ct);
                                    executed++;
                                }
                            }
                            break;

                        case ActionType.Delay:
                            if (!shadow && action.DelaySeconds > 0)
                            {
                                var delay = TimeSpan.FromSeconds(action.DelaySeconds);
                                await Task.Delay(delay < MaxDelay ? delay : MaxDelay, ct);
                            }
                            break;

                        case ActionType.Notify:
                            if (shadow)
                            {
                                _logger.LogInformation("[{Rule}] SHADOW notify: {Message}", rule.Name, action.Message);
                                wouldRun++;
                            }
                            else
                            {
                                // Fan the message out to every enabled delivery channel (Epic 2G); with none
                                // configured this logs only, so the box stays functional offline.
                                await _notifications.DispatchAsync(
                                    new NotificationMessage(rule.Name, action.Message ?? "", "info"), ct);
                                executed++;
                            }
                            break;
                    }
                }

                if (shadow)
                    detail = $"shadow: would run {wouldRun} action(s) (not executed)";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                success = false;
                detail = ex.Message;
                _logger.LogError(ex, "Error executing actions for rule {RuleId} ({Name})", rule.Id, rule.Name);
            }
        }

        await PublishHistory(rule, triggerSummary, conditionsMet, success, executed, detail);
    }

    private async Task PublishHistory(AutomationRule rule, string triggerSummary, bool conditionsMet, bool success, int executed, string? detail)
    {
        try
        {
            var envelope = Envelope<AutomationTriggeredV1>.Create(
                MessageTypes.AutomationTriggered,
                source: "automation-service",
                data: new AutomationTriggeredV1(
                    rule.Id, rule.Name, DateTimeOffset.UtcNow, conditionsMet, success, triggerSummary, executed, detail),
                subject: rule.Id);
            await _bus.PublishAsync(BusTopology.EventsExchange, BusTopology.AutomationTriggeredKey, envelope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing automation history for rule {RuleId}", rule.Id);
        }
    }
}
