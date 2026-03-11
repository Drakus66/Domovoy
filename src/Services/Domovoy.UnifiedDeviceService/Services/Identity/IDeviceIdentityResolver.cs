using System;
using Domovoy.Common.Models;

namespace Domovoy.UnifiedDeviceService.Services.Identity;

/// <summary>
/// Service responsible for mapping raw protocol identifiers (e.g., MQTT topics)
/// back to the logical Device IDs in the Domovoy system.
/// </summary>
public interface IDeviceIdentityResolver
{
    /// <summary>
    /// Registers a device in the resolver's cache using its metadata.
    /// </summary>
    void RegisterDevice(BaseEntity device);

    /// <summary>
    /// Removes a device from the resolver's cache.
    /// </summary>
    void UnregisterDevice(Guid deviceId);

    /// <summary>
    /// Resolves a raw protocol topic to a logical DeviceId.
    /// </summary>
    /// <param name="adapterSource">The name of the adapter (e.g., "Zigbee2Mqtt")</param>
    /// <param name="topic">The topic or identifier received (e.g., "zigbee2mqtt/living_room")</param>
    /// <returns>The Guid of the resolved device, or null if not found.</returns>
    Guid? ResolveDevice(string adapterSource, string topic);
}
