using Domovoy.Common.Models;

namespace Domovoy.DbGateway.Services
{
    public interface IDeviceRepository
    {
        Task<Device> GetDeviceAsync(string id);
        Task<IEnumerable<Device>> GetAllDevicesAsync();
        Task<Device> CreateDeviceAsync(Device device);
        Task UpdateDeviceAsync(string id, Device device);
        Task UpdateDeviceStateAsync(string id, Dictionary<string, object> state);
        Task DeleteDeviceAsync(string id);
    }
}