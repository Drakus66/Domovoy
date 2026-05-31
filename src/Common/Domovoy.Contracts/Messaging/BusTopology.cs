namespace Domovoy.Contracts.Messaging;

/// <summary>
/// Versioned <see cref="Envelope.Type"/> values. The <c>.vN</c> suffix is the contract version:
/// breaking changes introduce a new suffix so old and new consumers can coexist during migration.
/// </summary>
public static class MessageTypes
{
    public const string DeviceDiscovered = "domovoy.device.discovered.v1";
    public const string DeviceState = "domovoy.device.state.v1";
    public const string DeviceCommand = "domovoy.device.command.v1";
    public const string DeviceOnlineChanged = "domovoy.device.online.v1";
}

/// <summary>
/// Canonical bus topology — ONE naming convention (dotted <c>domovoy.&lt;domain&gt;</c> exchanges,
/// dotted routing keys). This is the migration target that replaces the mixed
/// <c>domovoy/discovery</c> (slash) / <c>device.commands</c> / <c>domovoy.state</c> constants
/// scattered across the codebase. All exchanges are AMQP topic exchanges.
/// </summary>
public static class BusTopology
{
    // Exchanges
    public const string DiscoveryExchange = "domovoy.discovery";
    public const string CommandsExchange = "domovoy.commands";
    public const string EventsExchange = "domovoy.events";
    public const string StateExchange = "domovoy.state";

    // Routing keys
    public const string DeviceDiscoveredKey = "device.discovered";
    public const string DeviceCommandKey = "device.command";
    public const string DeviceStateUpdatedKey = "device.state.updated";
    public const string DeviceOnlineChangedKey = "device.online.changed";
}
