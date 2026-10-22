// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Shared "fire a rule" path used by both the event engine and the scheduler: evaluate conditions now,
/// then run the actions on a background task so long <see cref="ActionType.Delay"/>/<see cref="ActionType.WaitForEvent"/>
/// steps never block the bus handler or the scheduler tick.
///
/// <para><b>Required expression (roadmap Epic 3E).</b> When a rule carries a live gate
/// (<see cref="AutomationRule.RequiredExpression"/>), it must hold both to start (folded into
/// <c>conditionsMet</c>, same as always) AND continuously while the run executes: this method subscribes to
/// <see cref="DeviceEventBroker"/> for the run's lifetime and cancels a linked token the moment the
/// expression stops holding — <see cref="ActionExecutor"/> is just handed that token like any other, so a
/// pending <c>Delay</c>/<c>WaitForEvent</c> unwinds via the normal <see cref="OperationCanceledException"/>
/// path. Rules without a gate are unaffected: no subscription, no linked token, identical to pre-3E behavior.</para>
/// </summary>
public sealed class RuleRunner
{
    private readonly RuleEvaluator _evaluator;
    private readonly RequiredExpressionEvaluator _requiredExpr;
    private readonly ActionExecutor _executor;
    private readonly DeviceEventBroker _broker;
    private readonly HomeModeState _mode;
    private readonly ILogger<RuleRunner> _logger;

    public RuleRunner(
        RuleEvaluator evaluator, RequiredExpressionEvaluator requiredExpr, ActionExecutor executor,
        DeviceEventBroker broker, HomeModeState mode, ILogger<RuleRunner> logger)
    {
        _evaluator = evaluator;
        _requiredExpr = requiredExpr;
        _executor = executor;
        _broker = broker;
        _mode = mode;
        _logger = logger;
    }

    public void Fire(AutomationRule rule, string triggerSummary, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var mode = _mode.Current;

        // Evaluate Mode conditions against the live home mode (roadmap Epic 1G), AND the required-expression
        // gate (Epic 3E) if the rule has one.
        var conditionsMet = _evaluator.ConditionsHold(rule.Conditions, now, mode)
                             && _requiredExpr.Holds(rule.RequiredExpression, now, mode);

        _logger.LogInformation("Rule '{Name}' triggered ({Summary}); conditions {Met}",
            rule.Name, triggerSummary, conditionsMet ? "met" : "not met");

        var runCt = ct;
        CancellationTokenSource? gateCts = null;
        Action? unsubscribe = null;

        if (conditionsMet && rule.RequiredExpression is { Conditions.Count: > 0 })
        {
            gateCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var localCts = gateCts;

            void OnStateChanged(Guid deviceId, string capabilityId, object? value)
            {
                if (!_requiredExpr.Holds(rule.RequiredExpression, DateTimeOffset.Now, _mode.Current))
                    localCts.Cancel();
            }

            _broker.StateChanged += OnStateChanged;
            unsubscribe = () => _broker.StateChanged -= OnStateChanged;
            runCt = gateCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _executor.ExecuteAsync(rule, triggerSummary, conditionsMet, runCt);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // runCt was cancelled but the host token wasn't — the required-expression gate closed mid-run.
                _logger.LogInformation("Rule '{Name}' cancelled — required expression no longer holds", rule.Name);
                await _executor.PublishCancelledAsync(rule, triggerSummary);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error running rule {RuleId}", rule.Id);
            }
            finally
            {
                unsubscribe?.Invoke();
                gateCts?.Dispose();
            }
        }, ct);
    }
}
