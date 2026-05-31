using System.Collections.Concurrent;

using Domovoy.Contracts.Capabilities;
using Domovoy.DeviceEmulator.Configuration;

namespace Domovoy.DeviceEmulator.Devices;

/// <summary>
/// A capability-native virtual device. It holds a set of <see cref="Capability"/> descriptors and a
/// current value per capability. Both server commands (MQTT <c>/set</c>) and the emulator UI converge
/// on <see cref="SetValue"/>, after which the engine republishes the device state.
/// </summary>
public sealed class VirtualDevice
{
    public string Id { get; }
    public string Name { get; }
    public string? Model { get; }
    public bool Simulate { get; }
    public IReadOnlyList<Capability> Capabilities { get; }

    /// <summary>
    /// Whether the device is currently announced to the server. While <c>false</c> the engine keeps the
    /// device in the UI but publishes nothing to the bus, so the server treats it as gone. Toggled by the
    /// register/unregister controls and set on the startup auto-announce.
    /// </summary>
    public bool Registered { get; set; }

    private readonly ConcurrentDictionary<string, object?> _state = new();

    public VirtualDevice(DeviceConfiguration cfg)
    {
        Id = cfg.Id;
        Name = cfg.Name;
        Model = cfg.Model;
        Simulate = cfg.Simulate;
        Capabilities = cfg.Capabilities.Select(ToCapability).ToList();

        foreach (var cap in Capabilities)
            _state[cap.Id] = DefaultValue(cap);
    }

    public IReadOnlyDictionary<string, object?> State => _state;

    public void SetValue(string capabilityId, object? value) => _state[capabilityId] = value;

    public bool HasCapability(string capabilityId) => _state.ContainsKey(capabilityId);

    public Capability? FindCapability(string id) => Capabilities.FirstOrDefault(c => c.Id == id);

    // ---- helpers ----------------------------------------------------------

    private static Capability ToCapability(CapabilityConfiguration c)
    {
        var kind = Enum.TryParse<CapabilityKind>(c.Kind, ignoreCase: true, out var k) ? k : CapabilityKind.Number;

        var attrs = new Dictionary<string, object?> { [CapabilityAttributeKeys.Writable] = c.Writable };
        if (c.Unit is not null) attrs[CapabilityAttributeKeys.Unit] = c.Unit;
        if (c.Min is not null) attrs[CapabilityAttributeKeys.Min] = c.Min;
        if (c.Max is not null) attrs[CapabilityAttributeKeys.Max] = c.Max;

        return new Capability(c.Id, kind, attrs);
    }

    private static object? DefaultValue(Capability cap) => cap.Kind switch
    {
        CapabilityKind.Boolean => false,
        CapabilityKind.Number => Midpoint(cap),
        _ => null
    };

    private static double Midpoint(Capability cap)
    {
        double? min = cap.Attributes.TryGetValue(CapabilityAttributeKeys.Min, out var mn) ? Convert.ToDouble(mn) : null;
        double? max = cap.Attributes.TryGetValue(CapabilityAttributeKeys.Max, out var mx) ? Convert.ToDouble(mx) : null;
        if (min.HasValue && max.HasValue) return Math.Round((min.Value + max.Value) / 2, 1);
        return min ?? 0;
    }
}
