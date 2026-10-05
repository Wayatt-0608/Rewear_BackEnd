using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho OrderStatusHistory - implementation với MongoDB (Task 7).
/// </summary>
public class OrderStatusHistoryRepository : IOrderStatusHistoryRepository
{
    private readonly IMongoCollection<OrderStatusHistory> _histories;

    public OrderStatusHistoryRepository(MongoDbContext context)
    {
        _histories = context.OrderStatusHistories;
    }

    public async Task CreateAsync(OrderStatusHistory history)
    {
        await _histories.InsertOneAsync(history);
    }

    public async Task<List<OrderStatusHistory>> GetByOrderIdAsync(string orderId)
    {
        return await _histories
            .Find(h => h.OrderId == orderId)
            .SortBy(h => h.CreatedAt)
            .ToListAsync();
    }
}