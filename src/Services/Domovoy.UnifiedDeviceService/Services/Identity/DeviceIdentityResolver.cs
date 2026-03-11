using System;
using System.Collections.Concurrent;
using Domovoy.Common.Models;

namespace Domovoy.UnifiedDeviceService.Services.Identity;

public class DeviceIdentityResolver : IDeviceIdentityResolver
{
    // Maps [AdapterName + "$" + Topic] -> DeviceId
    private readonly ConcurrentDictionary<string, Guid> _topicToDeviceMap = new();

    public void RegisterDevice(BaseEntity device)
    {
        if (device.Metadata == null) return;

        // The adapter source the device belongs to
        device.Metadata.TryGetValue("AdapterSource", out var source);
        if (string.IsNullOrEmpty(source))
        {
            // Backward compatibility: If no source, we might just try scanning StateTopic
            // But ideally all devices have an AdapterSource.
            source = "Unknown";
        }

        // We map by state_topic as that is usually what adapters publish on
        if (device.Metadata.TryGetValue("state_topic", out var stateTopic) && !string.IsNullOrEmpty(stateTopic))
        {
            var key = BuildKey(source, stateTopic);
            _topicToDeviceMap[key] = device.Id;
        }

        if (device.Metadata.TryGetValue("command_topic", out var commandTopic) && !string.IsNullOrEmpty(commandTopic))
        {
            var key = BuildKey(source, commandTopic);
            _topicToDeviceMap[key] = device.Id;
        }

        // Map by Hardware Address (IEEE) to retain identity across renames
        if (device.Metadata.TryGetValue("ieee_address", out var ieee) && !string.IsNullOrEmpty(ieee))
        {
            var key = BuildKey(source, ieee);
            _topicToDeviceMap[key] = device.Id;
        }
    }

    public void UnregisterDevice(Guid deviceId)
    {
        // Simplistic unregister: iterate and remove matching values
        var keysToRemove = new System.Collections.Generic.List<string>();
        foreach (var kvp in _topicToDeviceMap)
        {
            if (kvp.Value == deviceId)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _topicToDeviceMap.TryRemove(key, out _);
        }
    }

    public Guid? ResolveDevice(string adapterSource, string topic)
    {
        var key = BuildKey(adapterSource, topic);
        if (_topicToDeviceMap.TryGetValue(key, out var deviceId))
        {
            return deviceId;
        }
        
        // If not found with specific source, try looking it up generically if "Unknown" source
        var fallbackKey = BuildKey("Unknown", topic);
        if (_topicToDeviceMap.TryGetValue(fallbackKey, out var fallbackId))
        {
            return fallbackId;
        }

        return null;
    }

    private static string BuildKey(string source, string topic)
    {
        return $"{source}${topic}";
    }
}
