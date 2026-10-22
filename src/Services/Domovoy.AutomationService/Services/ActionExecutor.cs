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
///
/// <para><b>Branches (roadmap Epic 3E).</b> Actions run in order via <see cref="RunActionsAsync"/>, which is
/// also how <see cref="RuleAction.OnTimeout"/> (a <see cref="ActionType.WaitForEvent"/> that timed out) and
/// <see cref="RuleAction.OnError"/> (an action that threw) are executed — both branches are terminal: once
/// entered, the outer action sequence does not resume. This keeps the existing flat, ordered
/// <see cref="AutomationRule.Actions"/> list (no general action tree) while still giving every action an
/// optional escape hatch, matching Homey's "error card" model.</para>
/// </summary>
public sealed class ActionExecutor
{
    private readonly IMessageBus _bus;
    private readonly NotificationDispatcher _notifications;
    private readonly SceneStore _scenes;
    private readonly DeviceRegistry _registry;
    private readonly DeviceEventBroker _broker;
    private readonly TimeSpan _boundedCooldown;
    private readonly ILogger<ActionExecutor> _logger;

    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(24);
    private const int DefaultWaitTimeoutSeconds = 300;

    /// <summary>Per-rule last real execution — the throttle clock for BoundedActive rules (Epic 1F).</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFired = new();

    public ActionExecutor(
        IMessageBus bus, NotificationDispatcher notifications, SceneStore scenes, DeviceRegistry registry,
        DeviceEventBroker broker, IOptions<AutomationOptions> options, ILogger<ActionExecutor> logger)
    {
        _bus = bus;
        _notifications = notifications;
        _scenes = scenes;
        _registry = registry;
        _broker = broker;
        _boundedCooldown = TimeSpan.FromSeconds(Math.Max(0, options.Value.BoundedActiveCooldownSeconds));
        _logger = logger;
    }

    public async Task ExecuteAsync(AutomationRule rule, string triggerSummary, bool conditionsMet, CancellationToken ct)
    {
        var success = true;
        string? detail = null;
        var executed = 0;

        // Shadow rules (roadmap Epic 1F staged rollout): evaluate and log what they WOULD do, but publish
        // no commands and skip real delays/waits — the "shadow mode" stage before a rule (or ML proposal)
        // goes live.
        var shadow = rule.Status == RuleStatus.Shadow;

        // BoundedActive rules (Epic 1F, the stage between Shadow and Active): execute, but no more often than
        // the cooldown — bounding actuation rate (blast radius) while trust is still building. Active rules
        // are never throttled.
        if (conditionsMet && !shadow && rule.Status == RuleStatus.BoundedActive)
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
                var outcome = await RunActionsAsync(rule.Actions, rule, shadow, errorContext: null, ct);
                executed = outcome.Executed;
                if (shadow)
                    detail = $"shadow: would run {outcome.WouldRun} action(s) (not executed)";
                else if (outcome.ErrorHandled)
                    detail = $"handled error: {outcome.LastError}"; // on-error branch ran — not a run failure
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

    /// <summary>
    /// Publishes the run-history row for a rule whose actions were cancelled mid-run because its
    /// <see cref="RequiredExpression"/> (Epic 3E) stopped holding — called by <see cref="RuleRunner"/>,
    /// which is the one that can tell a required-expression cancellation apart from a host shutdown (both
    /// surface as <see cref="OperationCanceledException"/> from <see cref="ExecuteAsync"/>, which — like a
    /// real shutdown — publishes no history of its own so a half-run action list is never double-recorded).
    /// </summary>
    public Task PublishCancelledAsync(AutomationRule rule, string triggerSummary) =>
        PublishHistory(rule, triggerSummary, conditionsMet: true, success: false, executed: 0,
            detail: "cancelled: required expression no longer holds");

    /// <summary>Outcome of running one action list (top-level or a branch) — merged up through nested branches.</summary>
    private struct ActionRunOutcome
    {
        public int Executed;
        public int WouldRun;
        public bool ErrorHandled;
        public string? LastError;

        public void Merge(ActionRunOutcome branch)
        {
            Executed += branch.Executed;
            WouldRun += branch.WouldRun;
            ErrorHandled |= branch.ErrorHandled;
            LastError ??= branch.LastError;
        }
    }

    /// <summary>
    /// Runs one ordered action list — the top-level <see cref="AutomationRule.Actions"/>, or a branch
    /// (<see cref="RuleAction.OnTimeout"/>/<see cref="RuleAction.OnError"/>) recursed into from it. Branches
    /// are terminal: entering one returns immediately after it finishes, so the caller's remaining actions
    /// never resume. <paramref name="errorContext"/> is the caught exception's message while running an
    /// <c>OnError</c> branch (substituted for the <c>{error}</c> token in a <see cref="ActionType.Notify"/>
    /// message inside it); null everywhere else.
    /// </summary>
    private async Task<ActionRunOutcome> RunActionsAsync(
        IReadOnlyList<RuleAction> actions, AutomationRule rule, bool shadow, string? errorContext, CancellationToken ct)
    {
        var outcome = new ActionRunOutcome();

        foreach (var action in actions)
        {
            try
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
                                outcome.WouldRun++;
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
                                outcome.Executed++;
                            }
                        }
                        break;

                    case ActionType.Scene:
                        // Activate a stored scene (Epic 3B): fan its per-device targets out as commands,
                        // exactly like ActionType.Command does per device. Attribution stays with the rule
                        // (source automation:{ruleId}), so the event-log reads triggerSource=rule.
                        if (!string.IsNullOrEmpty(action.SceneId) && _scenes.Get(action.SceneId) is { } scene)
                        {
                            foreach (var target in scene.Targets)
                            {
                                if (!Guid.TryParse(target.DeviceId, out var sceneDeviceId) || target.Set is not { Count: > 0 })
                                    continue;

                                if (shadow)
                                {
                                    _logger.LogInformation("[{Rule}] SHADOW would activate scene '{Scene}' → {Device} ← {Set}",
                                        rule.Name, scene.Name, target.DeviceId, string.Join(", ", target.Set.Keys));
                                    outcome.WouldRun++;
                                }
                                else
                                {
                                    var envelope = Envelope<DeviceCommandV1>.Create(
                                        MessageTypes.DeviceCommand,
                                        source: $"automation:{rule.Id}",
                                        data: new DeviceCommandV1(sceneDeviceId, target.Set),
                                        subject: target.DeviceId,
                                        correlationId: rule.Id);
                                    await _bus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope, ct);
                                    outcome.Executed++;
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(action.SceneId))
                        {
                            _logger.LogWarning("[{Rule}] scene action references unknown scene {SceneId}", rule.Name, action.SceneId);
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
                            outcome.WouldRun++;
                        }
                        else
                        {
                            // {error} is substituted with the OnError-branch's caught exception message (the
                            // "error text as context" requirement); a no-op replace everywhere else.
                            var message = (action.Message ?? "").Replace("{error}", errorContext ?? "");
                            // Fan the message out to every enabled delivery channel (Epic 2G); with none
                            // configured this logs only, so the box stays functional offline.
                            await _notifications.DispatchAsync(
                                new NotificationMessage(rule.Name, message, "info"), ct);
                            outcome.Executed++;
                        }
                        break;

                    case ActionType.WaitForEvent:
                        // Shadow: don't actually block evaluation on a real-world event — log and move on,
                        // same tier as a shadow Delay/Command.
                        var matched = shadow || await WaitForEventAsync(action, ct);
                        if (shadow)
                            outcome.WouldRun++;
                        if (!matched && action.OnTimeout is { Count: > 0 })
                        {
                            _logger.LogInformation("[{Rule}] wait for {Capability} timed out after {Seconds}s — running OnTimeout branch",
                                rule.Name, action.WaitCapabilityId, ResolveTimeout(action.TimeoutSeconds));
                            var branch = await RunActionsAsync(action.OnTimeout, rule, shadow, errorContext, ct);
                            outcome.Merge(branch);
                            return outcome; // terminal: timeout branch replaces the rest of this sequence
                        }
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw; // shutdown or a required-expression gate closing — never treated as an action error
            }
            catch (Exception ex) when (action.OnError is { Count: > 0 })
            {
                _logger.LogWarning(ex, "[{Rule}] action {Type} failed — running OnError branch", rule.Name, action.Type);
                var branch = await RunActionsAsync(action.OnError, rule, shadow, ex.Message, ct);
                branch.ErrorHandled = true;
                branch.LastError = ex.Message;
                outcome.Merge(branch);
                return outcome; // terminal: error branch replaces the rest of this sequence
            }
            // No OnError branch: an unhandled action exception falls through and aborts the whole run,
            // exactly as before this action gained a Type at all — caught by ExecuteAsync's outer try/catch.
        }

        return outcome;
    }

    private static int ResolveTimeout(int configuredSeconds) =>
        Math.Clamp(configuredSeconds <= 0 ? DefaultWaitTimeoutSeconds : configuredSeconds, 1, (int)MaxDelay.TotalSeconds);

    /// <summary>
    /// Waits (event-driven, via <see cref="DeviceEventBroker"/> — not polling) for a device-state match or a
    /// timeout. Returns true when the match already holds or arrives in time, false on timeout. A real
    /// cancellation (host shutdown or a required-expression gate closing) still throws
    /// <see cref="OperationCanceledException"/> through the awaited <see cref="Task.Delay(TimeSpan,CancellationToken)"/>.
    /// </summary>
    private async Task<bool> WaitForEventAsync(RuleAction action, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(action.WaitCapabilityId)) return false;
        var capabilityId = action.WaitCapabilityId;

        bool Matches(Guid deviceId, string capId, object? value)
        {
            if (!string.Equals(capId, capabilityId, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(action.WaitDeviceId))
            {
                return Guid.TryParse(action.WaitDeviceId, out var wantId) && wantId == deviceId &&
                       ValueOps.Compare(value, action.WaitOperator, action.WaitValue);
            }
            if (!string.IsNullOrEmpty(action.WaitZoneId))
            {
                return string.Equals(_registry.GetZone(deviceId), action.WaitZoneId, StringComparison.OrdinalIgnoreCase) &&
                       ValueOps.Compare(value, action.WaitOperator, action.WaitValue);
            }
            return ValueOps.Compare(value, action.WaitOperator, action.WaitValue);
        }

        bool AlreadyMatches()
        {
            if (!string.IsNullOrEmpty(action.WaitDeviceId) && Guid.TryParse(action.WaitDeviceId, out var id))
                return Matches(id, capabilityId, _registry.GetValue(id, capabilityId));
            if (!string.IsNullOrEmpty(action.WaitZoneId))
                return _registry.DevicesInZone(action.WaitZoneId).Any(id => Matches(id, capabilityId, _registry.GetValue(id, capabilityId)));
            return false; // neither device nor zone bound ⇒ can't already hold, only observed going forward
        }

        if (AlreadyMatches()) return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(Guid deviceId, string capId, object? value)
        {
            if (Matches(deviceId, capId, value)) tcs.TrySetResult(true);
        }

        _broker.StateChanged += OnStateChanged;
        try
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ResolveTimeout(action.TimeoutSeconds)), ct);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completed == tcs.Task) return true;
            ct.ThrowIfCancellationRequested(); // the delay ended because ct fired, not because time ran out
            return false;
        }
        finally
        {
            _broker.StateChanged -= OnStateChanged;
        }
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
