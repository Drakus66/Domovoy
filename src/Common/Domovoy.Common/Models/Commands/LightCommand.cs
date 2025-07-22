using Domovoy.Common.Models.Enums;

namespace Domovoy.Common.Models.Commands;

public class LightCommand : BaseCommand
{
    public LightCommandTypes CommandTypes { get; set; }
}