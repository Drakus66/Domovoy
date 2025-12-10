using Domovoy.Common.Models.Enums;

namespace Domovoy.Common.Models.Commands
{
    public class DeviceCommand : BaseCommand
    {
        public DeviceCommandTypes CommandTypes { get; set; }
    }
}