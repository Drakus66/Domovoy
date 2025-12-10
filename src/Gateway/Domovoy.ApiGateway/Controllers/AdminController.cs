using Domovoy.Common.Models.Commands;
using Domovoy.MessageBus;

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IMessageBus messageBus, ILogger<AdminController> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Restarts a specific service by name
    /// </summary>
    [HttpPost("services/{serviceName}/restart")]
    public async Task<IActionResult> RestartService(string serviceName)
    {
        _logger.LogInformation("Admin requested restart for service: {ServiceName}", serviceName);

        var command = new OrchestrationCommand
        {
            ServiceName = serviceName,
            Action = OrchestrationAction.Restart,
            Source = "ApiGateway",
            Timestamp = DateTime.UtcNow
        };

        // Publish to orchestration exchange
        await _messageBus.PublishAsync(
            "domovoy.commands",
            "command.orchestration.restart",
            command);

        return Ok(new { message = $"Restart command sent to {serviceName}" });
    }

    /// <summary>
    /// Restarts all services
    /// </summary>
    [HttpPost("services/restart-all")]
    public async Task<IActionResult> RestartAllServices()
    {
        _logger.LogInformation("Admin requested restart for ALL services");

        var command = new OrchestrationCommand
        {
            ServiceName = "all",
            Action = OrchestrationAction.Restart,
            Source = "ApiGateway",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus.PublishAsync(
            "domovoy.commands",
            "command.orchestration.restart",
            command);

        return Ok(new { message = "Restart command sent to all services" });
    }
}
