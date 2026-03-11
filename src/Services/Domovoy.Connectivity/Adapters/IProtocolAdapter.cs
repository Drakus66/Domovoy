using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

public interface IProtocolAdapter
{
    string Name { get; }

    Task StartAsync(IMqttClient mqttClient, CancellationToken token);
    Task StopAsync(CancellationToken token);

    // Inbound (MQTT -> Bus)
    bool CanHandleTopic(string topic);
    Task HandleMessageAsync(string topic, string payload);

    // Outbound (Bus -> MQTT)
    Task HandleCommandAsync(DeviceCommand command);

    event Func<DeviceDiscoveredEvent, Task> OnDeviceDiscovered;
    event Func<AdapterStateReportedEvent, Task> OnAdapterStateReported;
}
