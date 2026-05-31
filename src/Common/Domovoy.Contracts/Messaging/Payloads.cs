namespace Domovoy.Contracts.Messaging;

using Domovoy.Contracts.Devices;

/// <summary>
/// Payloads carried inside <see cref="Envelope{T}.Data"/>. These are the wire contracts that the
/// migration moves the system onto; they are capability-addressed, not device-type-specific.
/// </summary>

/// <summary>A device was discovered (or re-announced) by an adapter. Carries the full descriptor.</summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceDiscovered"/>.</remarks>
public sealed record DeviceDiscoveredV1(DeviceDescriptor Device);

/// <summary>
/// A normalized device state update — capability id → value (already decoded by the adapter codec,
/// e.g. <c>brightness</c> 0..100, <c>on_off</c> bool). Replaces raw protocol payloads on the bus.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceState"/>.</remarks>
public sealed record DeviceStateReportV1(
    Guid DeviceId,
    IReadOnlyDictionary<string, object?> State);

/// <summary>
/// A capability-addressed command — set one or more capabilities (e.g. <c>on_off</c>=true,
/// <c>brightness</c>=50). The owning adapter encodes this into the protocol-native payload.
/// </summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceCommand"/>.</remarks>
public sealed record DeviceCommandV1(
    Guid DeviceId,
    IReadOnlyDictionary<string, object?> Set);

/// <summary>A device's reachability changed.</summary>
/// <remarks>Envelope type: <see cref="MessageTypes.DeviceOnlineChanged"/>.</remarks>
public sealed record DeviceOnlineChangedV1(
    Guid DeviceId,
    bool IsOnline);
