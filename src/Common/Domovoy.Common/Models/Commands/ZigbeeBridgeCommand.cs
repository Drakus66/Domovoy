namespace Domovoy.Common.Models.Commands;

public enum ZigbeeBridgeCommandType
{
    PermitJoin,
    RenameDevice,
    RemoveDevice,
}

public class ZigbeeBridgeCommand : BaseCommand
{
    public ZigbeeBridgeCommandType CommandType { get; set; }
    public int PermitJoinDuration { get; set; } = 254;
    public string TargetDevice { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}
