using Domovoy.ApiGateway.Services;
using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Commands;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ZigbeeController : ControllerBase
{
    private readonly ZigbeeBridgeStateCache _cache;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ZigbeeController> _logger;

    public ZigbeeController(
        ZigbeeBridgeStateCache cache,
        IMessageBus messageBus,
        ILogger<ZigbeeController> logger)
    {
        _cache = cache;
        _messageBus = messageBus;
        _logger = logger;
    }

    [HttpGet("bridge")]
    public IActionResult GetBridge()
    {
        return Ok(new
        {
            isOnline = _cache.IsOnline,
            version = _cache.Version,
            coordinator = new
            {
                type = _cache.CoordinatorType,
                address = _cache.CoordinatorAddress,
            },
            network = new
            {
                channel = _cache.Channel,
                panId = _cache.PanId,
            },
            permitJoin = _cache.PermitJoin,
            permitJoinTimeout = _cache.PermitJoinTimeout,
            lastUpdated = _cache.LastUpdated,
        });
    }

    [HttpGet("devices")]
    public IActionResult GetDevices()
    {
        var devices = _cache.GetDevices().Select(d => new
        {
            d.IeeeAddress,
            d.FriendlyName,
            d.Type,
            d.Supported,
            d.Model,
            d.Vendor,
            d.Description,
            d.State,
            d.LastSeen,
        });
        return Ok(devices);
    }

    [HttpPost("permit-join")]
    public async Task<IActionResult> PermitJoin([FromBody] PermitJoinRequest request)
    {
        var duration = Math.Clamp(request.Duration, 0, 254);
        var command = new ZigbeeBridgeCommand
        {
            CommandType = ZigbeeBridgeCommandType.PermitJoin,
            PermitJoinDuration = duration,
            Source = "ApiGateway",
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeCommandRoutingKey,
            command);

        _logger.LogInformation("PermitJoin command sent: duration={Duration}s", duration);
        return Ok(new { duration, message = duration > 0 ? $"Permit join enabled for {duration}s" : "Permit join disabled" });
    }

    [HttpPost("devices/{friendlyName}/rename")]
    public async Task<IActionResult> RenameDevice(string friendlyName, [FromBody] RenameDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest(new { error = "New name cannot be empty" });

        var command = new ZigbeeBridgeCommand
        {
            CommandType = ZigbeeBridgeCommandType.RenameDevice,
            TargetDevice = friendlyName,
            NewName = request.NewName.Trim(),
            Source = "ApiGateway",
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeCommandRoutingKey,
            command);

        _logger.LogInformation("Rename command: {From} -> {To}", friendlyName, request.NewName);
        return Ok(new { from = friendlyName, to = request.NewName.Trim() });
    }

    [HttpDelete("devices/{friendlyName}")]
    public async Task<IActionResult> RemoveDevice(string friendlyName)
    {
        var command = new ZigbeeBridgeCommand
        {
            CommandType = ZigbeeBridgeCommandType.RemoveDevice,
            TargetDevice = friendlyName,
            Source = "ApiGateway",
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeCommandRoutingKey,
            command);

        _logger.LogInformation("Remove device command: {FriendlyName}", friendlyName);
        return Ok(new { removed = friendlyName });
    }

    [HttpPost("devices/{friendlyName}/set")]
    public async Task<IActionResult> SetDeviceState(string friendlyName, [FromBody] Dictionary<string, object> payload)
    {
        var deviceCommand = new Domovoy.Common.Models.Commands.DeviceCommand
        {
            Source = "ApiGateway",
            CommandTypes = Domovoy.Common.Models.Enums.DeviceCommandTypes.SetState,
            Parameters = new Dictionary<string, object>(payload)
            {
                ["command_topic"] = $"zigbee2mqtt/{friendlyName}/set",
                ["AdapterSource"] = "Zigbee2Mqtt",
            },
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.DeviceCommandsExchange,
            MessageBusConfiguration.DeviceCommandsQueue,
            deviceCommand);

        return Ok();
    }
}

public record PermitJoinRequest(int Duration = 254);
public record RenameDeviceRequest(string NewName);
