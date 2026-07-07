// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Connectivity.Services;

public class ZigbeeBridgeInfo
{
    public bool IsOnline { get; set; }
    public string Version { get; set; } = string.Empty;
    public string CoordinatorType { get; set; } = string.Empty;
    public string CoordinatorAddress { get; set; } = string.Empty;
    public int Channel { get; set; }
    public int PanId { get; set; }
    public bool PermitJoin { get; set; }
    public int PermitJoinTimeout { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class ZigbeeDeviceInfo
{
    public string IeeeAddress { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Supported { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool InterviewCompleted { get; set; }
    public Dictionary<string, object> State { get; set; } = new();
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public class ZigbeeBridgeCache
{
    private readonly object _lock = new();
    private ZigbeeBridgeInfo _info = new();
    private readonly Dictionary<string, ZigbeeDeviceInfo> _devices = new();

    public ZigbeeBridgeInfo GetBridgeInfo()
    {
        lock (_lock) return _info;
    }

    public IReadOnlyList<ZigbeeDeviceInfo> GetDevices()
    {
        lock (_lock) return _devices.Values.ToList().AsReadOnly();
    }

    public void UpdateBridgeState(bool isOnline)
    {
        lock (_lock)
        {
            _info.IsOnline = isOnline;
            _info.LastUpdated = DateTime.UtcNow;
        }
    }

    public void UpdateBridgeInfo(ZigbeeBridgeInfo info)
    {
        lock (_lock)
        {
            info.IsOnline = _info.IsOnline;
            info.LastUpdated = DateTime.UtcNow;
            _info = info;
        }
    }

    public void UpdateDevices(IEnumerable<ZigbeeDeviceInfo> devices)
    {
        lock (_lock)
        {
            _devices.Clear();
            foreach (var d in devices)
                _devices[d.IeeeAddress] = d;
        }
    }

    public void UpdateDeviceState(string ieeeOrFriendlyName, Dictionary<string, object> state)
    {
        lock (_lock)
        {
            var device = _devices.Values.FirstOrDefault(d =>
                d.IeeeAddress == ieeeOrFriendlyName || d.FriendlyName == ieeeOrFriendlyName);
            if (device != null)
            {
                foreach (var kv in state)
                    device.State[kv.Key] = kv.Value;
                device.LastSeen = DateTime.UtcNow;
            }
        }
    }
}
