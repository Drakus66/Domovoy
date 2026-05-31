using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Device-state trigger engine (roadmap Epic 1A). Subscribes to <see cref="DeviceStateReportV1"/>,
/// keeps the <see cref="DeviceRegistry"/> live, and fires any active/protected rule whose device-state
/// trigger matches the change. Rule actions run on background tasks (see <see cref="RuleRunner"/>), so
/// "on for N minutes" patterns don't block the bus.
/// </summary>
public sealed class AutomationEngine : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly DeviceRegistry _registry;
    private readonly RuleStore _store;
    private readonly RuleEvaluator _evaluator;
    private readonly RuleRunner _runner;
    private readonly ILogger<AutomationEngine> _logger;

    public AutomationEngine(
        IMessageBus bus, DeviceRegistry registry, RuleStore store,
        RuleEvaluator evaluator, RuleRunner runner, ILogger<AutomationEngine> logger)
    {
        _bus = bus;
        _registry = registry;
        _store = store;
        _evaluator = evaluator;
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.SubscribeAsync<Envelope<DeviceStateReportV1>>(
            "automation-state",
            BusTopology.StateExchange,
            BusTopology.DeviceStateUpdatedKey,
            env => HandleStateReport(env, stoppingToken),
            stoppingToken);

        _logger.LogInformation("AutomationEngine subscribed to device state");
    }

    private Task HandleStateReport(Envelope<DeviceStateReportV1> envelope, CancellationToken ct)
    {
        var report = envelope.Data;
        if (report is null || report.State.Count == 0) return Task.CompletedTask;

        var fired = new HashSet<string>();

        foreach (var kv in report.State)
        {
            var oldValue = _registry.SetValue(report.DeviceId, kv.Key, kv.Value);
            var newValue = _registry.GetValue(report.DeviceId, kv.Key);

            foreach (var rule in ActiveRules())
            {
                if (fired.Contains(rule.Id)) continue;
                if (!rule.Triggers.Any(t => _evaluator.DeviceTriggerMatches(t, report.DeviceId, kv.Key, newValue, oldValue)))
                    continue;

                fired.Add(rule.Id);
                _runner.Fire(rule, $"{kv.Key}={Display(newValue)} on {report.DeviceId}", ct);
            }
        }

        return Task.CompletedTask;
    }

    private IEnumerable<AutomationRule> ActiveRules() =>
        _store.Rules.Where(r => r.IsProtected || r.Status == RuleStatus.Active);

    private static string Display(object? v) => v switch
    {
        null => "null",
        bool b => b ? "on" : "off",
        _ => v.ToString() ?? string.Empty
    };
}
