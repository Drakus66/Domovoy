using System;

namespace Domovoy.Common.Models.Events;

/// <summary>
/// Event emitted by connectivity adapters when they receive a raw state or message
/// from a device matching their protocol, before it is resolved to a specific logic device.
/// </summary>
public class AdapterStateReportedEvent : BaseEvent
{
    /// <summary>
    /// The name of the adapter that received the message (e.g. "Zigbee2Mqtt").
    /// </summary>
    public string AdapterSource { get; set; } = string.Empty;

    /// <summary>
    /// The physical identifier or topic the message came from (e.g. "zigbee2mqtt/my_lamp").
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// The raw payload string received on the topic.
    /// </summary>
    public string Payload { get; set; } = string.Empty;
}
