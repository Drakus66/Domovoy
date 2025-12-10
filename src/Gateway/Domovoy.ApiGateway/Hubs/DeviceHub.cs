using Microsoft.AspNetCore.SignalR;

namespace Domovoy.ApiGateway.Hubs;

/// <summary>
/// SignalR hub for pushing real-time device state updates to UI clients
/// </summary>
public class DeviceHub : Hub
{
    private readonly ILogger<DeviceHub> _logger;

    public DeviceHub(ILogger<DeviceHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Sends device state update to all connected clients
    /// </summary>
    public async Task SendDeviceStateUpdate(Guid deviceId, object state)
    {
        await Clients.All.SendAsync("DeviceStateUpdated", deviceId, state);
    }

    /// <summary>
    /// Sends device discovery notification to all connected clients
    /// </summary>
    public async Task SendDeviceDiscovered(Guid deviceId, string deviceType, string name)
    {
        await Clients.All.SendAsync("DeviceDiscovered", deviceId, deviceType, name);
    }
}
