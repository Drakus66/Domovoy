using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Shared "fire a rule" path used by both the event engine and the scheduler: evaluate conditions now,
/// then run the actions on a background task so long <see cref="ActionType.Delay"/> steps (e.g. "on for
/// 5 min") never block the bus handler or the scheduler tick.
/// </summary>
public sealed class RuleRunner
{
    private readonly RuleEvaluator _evaluator;
    private readonly ActionExecutor _executor;
    private readonly HomeModeState _mode;
    private readonly ILogger<RuleRunner> _logger;

    public RuleRunner(RuleEvaluator evaluator, ActionExecutor executor, HomeModeState mode, ILogger<RuleRunner> logger)
    {
        _evaluator = evaluator;
        _executor = executor;
        _mode = mode;
        _logger = logger;
    }

    public void Fire(AutomationRule rule, string triggerSummary, CancellationToken ct)
    {
        // Evaluate Mode conditions against the live home mode (roadmap Epic 1G).
        var conditionsMet = _evaluator.ConditionsHold(rule.Conditions, DateTimeOffset.Now, _mode.Current);

        _logger.LogInformation("Rule '{Name}' triggered ({Summary}); conditions {Met}",
            rule.Name, triggerSummary, conditionsMet ? "met" : "not met");

        _ = Task.Run(async () =>
        {
            try
            {
                await _executor.ExecuteAsync(rule, triggerSummary, conditionsMet, ct);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error running rule {RuleId}", rule.Id);
            }
        }, ct);
    }
}
