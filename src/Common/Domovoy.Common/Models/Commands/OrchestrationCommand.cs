namespace Domovoy.Common.Models.Commands;

/// <summary>
/// Command for orchestrating services (restart, update, etc.)
/// </summary>
public class OrchestrationCommand : BaseCommand
{
    /// <summary>
    /// Name of the service to orchestrate
    /// </summary>
    public required string ServiceName { get; set; }

    /// <summary>
    /// Action to perform (Restart, Update, Stop, Start)
    /// </summary>
    public required OrchestrationAction Action { get; set; }

    /// <summary>
    /// Optional version for Update action
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>
/// Orchestration actions
/// </summary>
public enum OrchestrationAction
{
    Restart,
    Update,
    Stop,
    Start,
    HealthCheck
}
