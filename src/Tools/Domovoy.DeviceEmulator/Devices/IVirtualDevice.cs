namespace Domovoy.DeviceEmulator.Devices;

public interface IVirtualDevice
{
    string Id { get; }
    string Name { get; }
    string DeviceType { get; }

    Task InitializeAsync(MQTTnet.Client.IMqttClient mqttClient);
    Task SimulateAsync(CancellationToken cancellationToken);
    Task HandleCommandAsync(string topic, string payload);
}
