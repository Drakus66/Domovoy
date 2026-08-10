// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Events;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.SignalR;
using Domovoy.ApiGateway.Hubs;

namespace Domovoy.ApiGateway.Services;

public class ZigbeeDeviceCacheEntry
{
    public string IeeeAddress { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Supported { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> State { get; set; } = new();
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public class ZigbeeBridgeStateCache
{
    private readonly object _lock = new();

    public bool IsOnline { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public string CoordinatorType { get; private set; } = string.Empty;
    public string CoordinatorAddress { get; private set; } = string.Empty;
    public int Channel { get; private set; }
    public int PanId { get; private set; }
    public bool PermitJoin { get; private set; }
    public int PermitJoinTimeout { get; private set; }
    public DateTime LastUpdated { get; private set; } = DateTime.UtcNow;

    private readonly Dictionary<string, ZigbeeDeviceCacheEntry> _devices = new();

    public void ApplyBridgeState(ZigbeeBridgeStateEvent ev)
    {
        lock (_lock)
        {
            IsOnline = ev.IsOnline;
            LastUpdated = DateTime.UtcNow;
        }
    }

    public void ApplyBridgeInfo(ZigbeeBridgeInfoEvent ev)
    {
        lock (_lock)
        {
            Version = ev.Version;
            CoordinatorType = ev.CoordinatorType;
            CoordinatorAddress = ev.CoordinatorAddress;
            Channel = ev.Channel;
            PanId = ev.PanId;
            PermitJoin = ev.PermitJoin;
            PermitJoinTimeout = ev.PermitJoinTimeout;
            LastUpdated = DateTime.UtcNow;
        }
    }

    public void ApplyDeviceState(string ieeeOrName, Dictionary<string, object> state)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(ieeeOrName, out var dev))
            {
                foreach (var kv in state) dev.State[kv.Key] = kv.Value;
                dev.LastSeen = DateTime.UtcNow;
            }
        }
    }

    public void RegisterDevice(ZigbeeDeviceCacheEntry entry)
    {
        lock (_lock) _devices[entry.IeeeAddress] = entry;
    }

    public IReadOnlyList<ZigbeeDeviceCacheEntry> GetDevices()
    {
        lock (_lock) return _devices.Values.ToList().AsReadOnly();
    }
}

public class ZigbeeBridgeCacheUpdater : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly ZigbeeBridgeStateCache _cache;
    private readonly IHubContext<DeviceHub> _hub;
    private readonly ILogger<ZigbeeBridgeCacheUpdater> _logger;

    public ZigbeeBridgeCacheUpdater(
        IMessageBus messageBus,
        ZigbeeBridgeStateCache cache,
        IHubContext<DeviceHub> hub,
        ILogger<ZigbeeBridgeCacheUpdater> logger)
    {
        _messageBus = messageBus;
        _cache = cache;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Awaited, with the stopping token — see EventRelayService for why fire-and-forget here is a trap.
        await _messageBus.SubscribeAsync<ZigbeeBridgeStateEvent>(
            "apigateway-zigbee-state",
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeStateRoutingKey,
            HandleBridgeState,
            stoppingToken);

        await _messageBus.SubscribeAsync<ZigbeeBridgeInfoEvent>(
            "apigateway-zigbee-info",
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeInfoRoutingKey,
            HandleBridgeInfo,
            stoppingToken);

        await _messageBus.SubscribeAsync<ZigbeeNetworkEvent>(
            "apigateway-zigbee-network",
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeNetworkEventRoutingKey,
            HandleNetworkEvent,
            stoppingToken);

        _logger.LogInformation("ZigbeeBridgeCacheUpdater subscriptions active");
    }

    private async Task HandleBridgeState(ZigbeeBridgeStateEvent ev)
    {
        _cache.ApplyBridgeState(ev);
        await _hub.Clients.All.SendAsync("ZigbeeBridgeStateChanged", ev.IsOnline);
        _logger.LogInformation("Zigbee bridge {State}", ev.IsOnline ? "online" : "offline");
    }

    private async Task HandleBridgeInfo(ZigbeeBridgeInfoEvent ev)
    {
        _cache.ApplyBridgeInfo(ev);
        await _hub.Clients.All.SendAsync("ZigbeeBridgeInfoUpdated", new
        {
            ev.Version,
            ev.CoordinatorType,
            ev.Channel,
            ev.PanId,
            ev.PermitJoin,
            ev.PermitJoinTimeout,
        });
    }

    private async Task HandleNetworkEvent(ZigbeeNetworkEvent ev)
    {
        await _hub.Clients.All.SendAsync("ZigbeeNetworkEvent", new
        {
            ev.EventType,
            ev.FriendlyName,
            ev.IeeeAddress,
        });
        _logger.LogInformation("Zigbee network event relayed: {Type} {Name}", ev.EventType, ev.FriendlyName);
    }
}
