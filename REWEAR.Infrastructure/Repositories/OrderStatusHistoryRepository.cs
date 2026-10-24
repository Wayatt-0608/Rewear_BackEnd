using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho OrderStatusHistory - implementation với MongoDB (Task 7).
/// </summary>
public class OrderStatusHistoryRepository : IOrderStatusHistoryRepository
{
    private readonly TransactionalCollection<OrderStatusHistory> _histories;
    private readonly TransactionalCollection<BsonDocument> _rawHistories;

    public OrderStatusHistoryRepository(MongoDbContext context)
    {
        _histories = context.OrderStatusHistories;
        _rawHistories = context.Collection<BsonDocument>("OrderStatusHistories");
    }

    public async Task CreateAsync(OrderStatusHistory history)
    {
        await _histories.InsertOneAsync(history);
    }

    public async Task<List<OrderStatusHistory>> GetByOrderIdAsync(string orderId)
    {
        var documents = await _rawHistories.Find(new BsonDocument("orderId", orderId)).Sort(new BsonDocument("createdAt", 1)).ToListAsync();
        return documents.Select(document =>
        {
            var history = BsonSerializer.Deserialize<OrderStatusHistory>(document);
            if (!document.TryGetValue("createdAt", out var at) || !at.IsBsonDateTime) history.CreatedAt = DateTime.MinValue;
            return history;
        }).ToList();
    }
}
