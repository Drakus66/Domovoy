using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

/// <summary>
/// A protocol adapter owns inbound MQTT topics for one ecosystem (Zigbee2MQTT, Domovoy Native, …)
/// and bridges them onto the capability contract. Outbound commands are received directly on the bus
/// by the adapter (subscribed as <c>Envelope&lt;DeviceCommandV1&gt;</c>) — no routing through the host.
/// </summary>
public interface IProtocolAdapter
{
    string Name { get; }

    Task StartAsync(IMqttClient mqttClient, CancellationToken token);
    Task StopAsync(CancellationToken token);

    /// <summary>True if this adapter owns the given MQTT topic.</summary>
    bool CanHandleTopic(string topic);

    /// <summary>Dispatch an inbound MQTT message (already routed by <see cref="CanHandleTopic"/>).</summary>
    Task HandleMessageAsync(string topic, string payload);
}
