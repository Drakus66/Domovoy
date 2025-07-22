using Domovoy.DbGateway.Models;

using MongoDB.Bson;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Repositories;

public interface IDeviceRepository : IBaseRepository<Device>
{
    Task<IEnumerable<Device>> GetByLocationAsync(string location);
    Task<IEnumerable<Device>> GetByTypeAsync(string type);
    Task UpdateStateAsync(string id, string state);
    Task UpdateOnlineStatusAsync(string id, bool isOnline);
}

public class DeviceRepository(IMongoDatabase database, ILogger<DeviceRepository> logger)
    : BaseRepository<Device>(database, "devices", logger), IDeviceRepository
{
    public async Task<IEnumerable<Device>> GetByLocationAsync(string location)
    {
        try
        {
            var filter = Builders<Device>.Filter.Eq(d => d.LocationId, location);
            return await _collection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting devices by location {Location}", location);
            throw;
        }
    }

    public async Task<IEnumerable<Device>> GetByTypeAsync(string type)
    {
        try
        {
            var filter = Builders<Device>.Filter.Eq(d => d.Type, type);
            return await _collection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting devices by type {Type}", type);
            throw;
        }
    }

    public Task UpdateStateAsync(string id, Dictionary<string, object> state)
    {
        throw new NotImplementedException();
    }

    public async Task UpdateStateAsync(string id, string state)
    {
        try
        {
            var filter = Builders<Device>.Filter.Eq("_id", ObjectId.Parse(id));
            var update = Builders<Device>.Update
                .Set(d => d.Status, state)
                .Set(d => d.LastSeen, DateTime.UtcNow);

            await _collection.UpdateOneAsync(filter, update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating state for device {Id}", id);
            throw;
        }
    }

    public async Task UpdateOnlineStatusAsync(string id, bool isOnline)
    {
        try
        {
            var filter = Builders<Device>.Filter.Eq("_id", ObjectId.Parse(id));
            var update = Builders<Device>.Update
                .Set(d => d.IsOnline, isOnline)
                .Set(d => d.LastSeen, DateTime.UtcNow);

            await _collection.UpdateOneAsync(filter, update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating online status for device {Id}", id);
            throw;
        }
    }
}
