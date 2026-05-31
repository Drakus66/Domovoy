using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Capability-addressed device control. Publishes <see cref="DeviceCommandV1"/> on the canonical bus
/// topology; the owning adapter (Zigbee2MQTT, Domovoy Native) encodes the capability set into the
/// device's protocol and applies it.
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

    /// <summary>
    /// Apply a capability set to a device, e.g. body: <c>{ "on_off": true, "brightness": 50 }</c>.
    /// </summary>
    [HttpPost("{id}/set")]
    public async Task<IActionResult> SetCapabilities(string id, [FromBody] Dictionary<string, JsonElement> set)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = $"deviceId '{id}' is not a valid GUID" });
        if (set is null || set.Count == 0)
            return BadRequest(new { error = "a non-empty capability set is required" });

        var normalized = set.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));

        var envelope = Envelope<DeviceCommandV1>.Create(
            MessageTypes.DeviceCommand,
            source: "apigateway",
            data: new DeviceCommandV1(deviceId, normalized),
            subject: id);

        await _messageBus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope);

        _logger.LogInformation("Capability command published for {DeviceId}: {Caps}", deviceId, string.Join(", ", set.Keys));
        return Accepted(new { deviceId = id, capabilities = set.Keys });
    }

    private static object? Normalize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        _ => null
    };
}
