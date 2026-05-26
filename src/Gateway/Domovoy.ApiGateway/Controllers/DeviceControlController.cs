using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Enums;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Generic device control endpoint used by the WebUI dashboard.
/// Publishes a <see cref="DeviceCommand"/> to the message bus with a "command.*" routing key
/// so that UnifiedDeviceManager receives it, enriches it with the device's stored metadata
/// (command_topic, adapter source, …) and forwards it to the Connectivity service.
///
/// This replaces the old Ocelot route that proxied /api/device-control to the now-removed
/// standalone DeviceService.
/// </summary>
[ApiController]
[Route("api/device-control")]
public class DeviceControlController : ControllerBase
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<DeviceControlController> _logger;

    public DeviceControlController(IMessageBus messageBus, ILogger<DeviceControlController> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    [HttpPost("command")]
    public async Task<IActionResult> SendCommand([FromBody] DeviceControlRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest(new { error = "deviceId is required" });

        if (!Guid.TryParse(request.DeviceId, out var deviceId))
            return BadRequest(new { error = $"deviceId '{request.DeviceId}' is not a valid GUID" });

        var command = new DeviceCommand
        {
            DeviceId = deviceId,
            CommandTypes = MapCommandType(request.Command),
            Parameters = request.Parameters is null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(request.Parameters),
            Source = "ApiGateway",
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.DeviceCommandsExchange,
            MessageBusConfiguration.DeviceControlCommandRoutingKey,
            command);

        _logger.LogInformation(
            "Device control command {Command} ({CommandType}) published for device {DeviceId}",
            request.Command, command.CommandTypes, deviceId);

        return Accepted(new { correlationId = command.CorrelationId, deviceId = request.DeviceId });
    }

    private static DeviceCommandTypes MapCommandType(string? command) =>
        (command ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "getstate" => DeviceCommandTypes.GetState,
            "updateconfiguration" or "configure" => DeviceCommandTypes.UpdateConfiguration,
            "identify" => DeviceCommandTypes.Identify,
            // toggle / setbrightness / setcolor / setstate / on / off / … are all state changes
            _ => DeviceCommandTypes.SetState,
        };
}

/// <summary>Matches the WebUI <c>DeviceCommand</c> payload: { deviceId, command, parameters }.</summary>
public record DeviceControlRequest(
    string DeviceId,
    string? Command,
    Dictionary<string, object>? Parameters);
