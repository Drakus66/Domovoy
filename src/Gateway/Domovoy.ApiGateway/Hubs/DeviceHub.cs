// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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

    /// <summary>
    /// Sends Zigbee bridge online/offline state to all connected clients
    /// </summary>
    public async Task SendZigbeeBridgeStateChanged(bool isOnline)
    {
        await Clients.All.SendAsync("ZigbeeBridgeStateChanged", isOnline);
    }

    /// <summary>
    /// Sends Zigbee permit-join status update (active flag + remaining seconds)
    /// </summary>
    public async Task SendZigbeePermitJoinChanged(bool active, int remainingSeconds)
    {
        await Clients.All.SendAsync("ZigbeePermitJoinChanged", active, remainingSeconds);
    }

    /// <summary>
    /// Sends a Zigbee network event (device_joined, device_leave, etc.)
    /// </summary>
    public async Task SendZigbeeNetworkEvent(string eventType, string friendlyName, string ieeeAddress)
    {
        await Clients.All.SendAsync("ZigbeeNetworkEvent", eventType, friendlyName, ieeeAddress);
    }
}
