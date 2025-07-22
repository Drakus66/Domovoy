using System.Linq.Expressions;

using MongoDB.Driver;
using MongoDB.Bson;

namespace Domovoy.DbGateway.Repositories;

public abstract class BaseRepository<T> : IBaseRepository<T> where T : class
{
    protected readonly IMongoCollection<T> _collection;
    protected readonly ILogger<BaseRepository<T>> _logger;

    protected BaseRepository(IMongoDatabase database, string collectionName, ILogger<BaseRepository<T>> logger)
    {
        _collection = database.GetCollection<T>(collectionName);
        _logger = logger;
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        try
        {
            return await _collection.Find(_ => true).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all documents from collection {CollectionName}",
                _collection.CollectionNamespace.CollectionName);
            throw;
        }
    }

    public virtual async Task<T?> GetByIdAsync(string id)
    {
        try
        {
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document with ID {Id} from collection {CollectionName}", id,
                _collection.CollectionNamespace.CollectionName);
            throw;
        }
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> filter)
    {
        try
        {
            return await _collection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding documents in collection {CollectionName}", _collection.CollectionNamespace.CollectionName);
            throw;
        }
    }

    public virtual async Task CreateAsync(T entity)
    {
        try
        {
            await _collection.InsertOneAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating document in collection {CollectionName}", _collection.CollectionNamespace.CollectionName);
            throw;
        }
    }

    public virtual async Task UpdateAsync(string id, T entity)
    {
        try
        {
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            await _collection.ReplaceOneAsync(filter, entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document with ID {Id} in collection {CollectionName}", id,
                _collection.CollectionNamespace.CollectionName);
            throw;
        }
    }

    public virtual async Task DeleteAsync(string id)
    {
        try
        {
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            await _collection.DeleteOneAsync(filter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document with ID {Id} from collection {CollectionName}", id,
                _collection.CollectionNamespace.CollectionName);
            throw;
        }
    }
}