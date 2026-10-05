using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Cart - implementation với MongoDB.
/// </summary>
public class CartRepository : ICartRepository
{
    private readonly IMongoCollection<Cart> _carts;

    public CartRepository(MongoDbContext context)
    {
        _carts = context.Carts;
    }

    public async Task<Cart?> GetByUserIdAsync(string userId)
    {
        return await _carts.Find(c => c.UserId == userId).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(Cart cart)
    {
        await _carts.InsertOneAsync(cart);
    }

    public async Task UpdateAsync(Cart cart)
    {
        cart.UpdatedAt = DateTime.UtcNow;
        await _carts.ReplaceOneAsync(c => c.Id == cart.Id, cart);
    }

    public async Task DeleteByUserIdAsync(string userId)
    {
        await _carts.DeleteOneAsync(c => c.UserId == userId);
    }

    public async Task RefreshExpirationAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var update = Builders<Cart>.Update
            .Set(c => c.UpdatedAt, now)
            .Set(c => c.ExpiresAt, now.AddDays(Cart.EXPIRATION_DAYS));

        await _carts.UpdateOneAsync(c => c.UserId == userId, update);
    }
}
