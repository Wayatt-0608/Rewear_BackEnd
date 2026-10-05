using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Order - implementation với MongoDB.
/// </summary>
public class OrderRepository : IOrderRepository
{
    private readonly IMongoCollection<Order> _orders;

    public OrderRepository(MongoDbContext context)
    {
        _orders = context.Orders;
    }

    public async Task<Order?> GetByIdAsync(string id)
    {
        return await _orders.Find(o => o.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Order?> GetByOrderCodeAsync(string orderCode)
    {
        return await _orders.Find(o => o.OrderCode == orderCode).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(Order order)
    {
        await _orders.InsertOneAsync(order);
    }

    public async Task UpdateAsync(Order order)
    {
        order.UpdatedAt = DateTime.UtcNow;
        await _orders.ReplaceOneAsync(o => o.Id == order.Id, order);
    }

    public async Task<List<Order>> GetByUserIdAsync(string userId, OrderStatus? status)
    {
        var filter = BuildFilter(userId, status);

        return await _orders
            .Find(filter)
            .SortByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<Order> Orders, long TotalCount)> GetPagedByUserIdAsync(
        string userId, OrderStatus? status, int page, int pageSize)
    {
        var filter = BuildFilter(userId, status);

        long totalCount = await _orders.CountDocumentsAsync(filter);

        var orders = await _orders
            .Find(filter)
            .SortByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (orders, totalCount);
    }

    public async Task<long> CountByStatusAsync(OrderStatus status)
    {
        return await _orders.CountDocumentsAsync(o => o.Status == status);
    }

    /// <summary>
    /// Ghép filter: theo userId và (nếu có) theo trạng thái.
    /// </summary>
    private static FilterDefinition<Order> BuildFilter(string userId, OrderStatus? status)
    {
        var filters = new List<FilterDefinition<Order>>
        {
            Builders<Order>.Filter.Eq(o => o.UserId, userId)
        };

        if (status.HasValue)
        {
            filters.Add(Builders<Order>.Filter.Eq(o => o.Status, status.Value));
        }

        return Builders<Order>.Filter.And(filters);
    }
}