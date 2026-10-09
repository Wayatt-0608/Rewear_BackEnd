using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Shipping - implementation với MongoDB (Task 9 + Task Shipper).
/// </summary>
public class ShippingRepository : IShippingRepository
{
    private readonly IMongoCollection<Shipping> _shippings;

    public ShippingRepository(MongoDbContext context)
    {
        _shippings = context.Shippings;
    }

    public async Task CreateAsync(Shipping shipping)
    {
        await _shippings.InsertOneAsync(shipping);
    }

    public async Task UpdateAsync(Shipping shipping)
    {
        shipping.UpdatedAt = DateTime.UtcNow;
        await _shippings.ReplaceOneAsync(s => s.Id == shipping.Id, shipping);
    }

    public async Task<Shipping?> GetByOrderIdAsync(string orderId)
    {
        return await _shippings.Find(s => s.OrderId == orderId).FirstOrDefaultAsync();
    }

    public async Task<List<Shipping>> GetByShipperIdAsync(string shipperId, ShippingStatus? status)
    {
        var filters = new List<FilterDefinition<Shipping>>
        {
            Builders<Shipping>.Filter.Eq(s => s.ShipperId, shipperId)
        };

        if (status.HasValue)
        {
            filters.Add(Builders<Shipping>.Filter.Eq(s => s.Status, status.Value));
        }

        return await _shippings
            .Find(Builders<Shipping>.Filter.And(filters))
            .SortByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<List<Shipping>> GetAllAsync(ShippingStatus? status)
    {
        var filter = status.HasValue
            ? Builders<Shipping>.Filter.Eq(s => s.Status, status.Value)
            : Builders<Shipping>.Filter.Empty;

        return await _shippings
            .Find(filter)
            .SortByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }
}
