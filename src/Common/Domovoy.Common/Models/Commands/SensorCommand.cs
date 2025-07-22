using Domovoy.Common.Models.Enums;

namespace Domovoy.Common.Models.Commands;

public class SensorCommand : BaseCommand
{
    public SensorCommandTypes CommandTypes { get; set; }
}