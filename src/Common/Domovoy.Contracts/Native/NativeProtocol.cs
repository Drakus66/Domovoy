namespace Domovoy.Contracts.Native;

/// <summary>
/// The Domovoy Native MQTT protocol (v1) spoken by DIY devices (Arduino/microcontroller firmware in
/// <c>Arduino/</c>) and by the device emulator. Unlike third-party protocols (Zigbee2MQTT), this is
/// OUR protocol, so it is <b>capability-native</b>: devices announce <c>Capability</c> descriptors and
/// exchange already-normalized capability values — no server-side codec/scaling is needed.
/// <para>Topics: <c>domovoy/native/&lt;deviceId&gt;/{announce|state|set|availability}</c>, where
/// <c>deviceId</c> is the device's stable hardware id (MAC / chip id).</para>
/// </summary>
public static class NativeProtocol
{
    /// <summary>Adapter source name; also the key used to derive the logical device id.</summary>
    public const string AdapterSource = "DomovoyNative";

    /// <summary>Topic root for the native protocol.</summary>
    public const string Root = "domovoy/native";

    // Per-device topics ----------------------------------------------------

    /// <summary>Retained device→server discovery announcement (payload: <see cref="NativeAnnounceV1"/>).</summary>
    public static string AnnounceTopic(string deviceId) => $"{Root}/{deviceId}/announce";

    /// <summary>Device→server state report (payload: capabilityId → normalized value).</summary>
    public static string StateTopic(string deviceId) => $"{Root}/{deviceId}/state";

    /// <summary>Server→device command (payload: capabilityId → value).</summary>
    public static string SetTopic(string deviceId) => $"{Root}/{deviceId}/set";

    /// <summary>Retained device availability (LWT): <see cref="Online"/> / <see cref="Offline"/>.</summary>
    public static string AvailabilityTopic(string deviceId) => $"{Root}/{deviceId}/availability";

    // Board ("hub") reachability ------------------------------------------

    /// <summary>Topic root for board reachability (a board is the MQTT connection fronting many devices).</summary>
    public const string HubRoot = "domovoy/hub";

    /// <summary>
    /// Retained board reachability, and the board's single MQTT Last-Will (payload <see cref="Online"/> /
    /// <see cref="Offline"/>). MQTT allows only ONE last-will per connection, so it cannot be attached to a
    /// per-device <see cref="AvailabilityTopic"/>; instead the will lives here and the adapter marks every
    /// device fronted by the hub offline when the board drops ungracefully.
    /// </summary>
    public static string HubStatusTopic(string hubId) => $"{HubRoot}/{hubId}/status";

    /// <summary>
    /// Server→all-devices broadcast: "who is there?". Devices re-publish their <see cref="AnnounceTopic"/>
    /// in response. The server sends this when its adapter (re)starts, because RabbitMQ's MQTT plugin does
    /// NOT redeliver retained announces to a wildcard subscription, so a restarted server would otherwise
    /// never relearn the deviceId→hardwareId mapping needed to route commands.
    /// </summary>
    public const string DiscoverTopic = "domovoy/native/discover";

    // Subscription filters (server side) -----------------------------------

    public const string AnnounceFilter = "domovoy/native/+/announce";
    public const string StateFilter = "domovoy/native/+/state";
    public const string AvailabilityFilter = "domovoy/native/+/availability";
    public const string HubStatusFilter = "domovoy/hub/+/status";

    // Availability payloads ------------------------------------------------

    public const string Online = "online";
    public const string Offline = "offline";

    /// <summary>Extracts the <c>deviceId</c> segment from a <c>domovoy/native/{deviceId}/...</c> topic, or null.</summary>
    public static string? DeviceIdFromTopic(string topic)
    {
        var parts = topic.Split('/');
        return parts.Length >= 4 && parts[0] == "domovoy" && parts[1] == "native"
            ? parts[2]
            : null;
    }

    /// <summary>Extracts the <c>hubId</c> segment from a <c>domovoy/hub/{hubId}/status</c> topic, or null.</summary>
    public static string? HubIdFromStatusTopic(string topic)
    {
        var parts = topic.Split('/');
        return parts.Length == 4 && parts[0] == "domovoy" && parts[1] == "hub" && parts[3] == "status"
            ? parts[2]
            : null;
    }
}
