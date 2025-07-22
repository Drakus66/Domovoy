using Domovoy.Common.Models.Enums.EntityTypes;

namespace Domovoy.Common.Models
{
    public class Device : BaseEntity
    {
        public GlobalEntityTypes Type { get; set; }
        public Dictionary<string, object> State { get; set; } = [];
    }
}