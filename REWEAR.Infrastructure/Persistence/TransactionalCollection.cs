using System.Linq.Expressions;
using MongoDB.Driver;

namespace REWEAR.Infrastructure.Persistence;

/// <summary>Enlists existing repositories in the current Mongo transaction, without changing their query API.</summary>
public sealed class TransactionalCollection<T>
{
    private readonly IMongoCollection<T> _collection;
    private readonly Func<IClientSessionHandle?> _session;
    public TransactionalCollection(IMongoCollection<T> collection, Func<IClientSessionHandle?> session)
    { _collection = collection; _session = session; }

    public IFindFluent<T, T> Find(Expression<Func<T, bool>> filter) => Find(new ExpressionFilterDefinition<T>(filter));
    public IAggregateFluent<T> Aggregate() => _session() is { } s ? _collection.Aggregate(s) : _collection.Aggregate();
    public IFindFluent<T, T> Find(FilterDefinition<T> filter) => _session() is { } s ? _collection.Find(s, filter) : _collection.Find(filter);
    public Task InsertOneAsync(T value) => _session() is { } s ? _collection.InsertOneAsync(s, value) : _collection.InsertOneAsync(value);
    public Task<ReplaceOneResult> ReplaceOneAsync(Expression<Func<T, bool>> filter, T value) => ReplaceOneAsync(new ExpressionFilterDefinition<T>(filter), value);
    public Task<ReplaceOneResult> ReplaceOneAsync(FilterDefinition<T> filter, T value) => _session() is { } s ? _collection.ReplaceOneAsync(s, filter, value) : _collection.ReplaceOneAsync(filter, value);
    public Task<UpdateResult> UpdateOneAsync(Expression<Func<T, bool>> filter, UpdateDefinition<T> update) => UpdateOneAsync(new ExpressionFilterDefinition<T>(filter), update);
    public Task<UpdateResult> UpdateOneAsync(FilterDefinition<T> filter, UpdateDefinition<T> update) => _session() is { } s ? _collection.UpdateOneAsync(s, filter, update) : _collection.UpdateOneAsync(filter, update);
    public Task<UpdateResult> UpdateManyAsync(Expression<Func<T, bool>> filter, UpdateDefinition<T> update) => _session() is { } s ? _collection.UpdateManyAsync(s, filter, update) : _collection.UpdateManyAsync(filter, update);
    public Task<DeleteResult> DeleteOneAsync(Expression<Func<T, bool>> filter) => _session() is { } s ? _collection.DeleteOneAsync(s, filter) : _collection.DeleteOneAsync(filter);
    public Task<long> CountDocumentsAsync(Expression<Func<T, bool>> filter) => CountDocumentsAsync(new ExpressionFilterDefinition<T>(filter));
    public Task<long> CountDocumentsAsync(FilterDefinition<T> filter) => _session() is { } s ? _collection.CountDocumentsAsync(s, filter) : _collection.CountDocumentsAsync(filter);
}
