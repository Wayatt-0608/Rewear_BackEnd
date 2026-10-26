using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho WishlistItem - implementation với MongoDB.
/// </summary>
/// <remarks>
/// Duy trì tối thiểu số round-trip: mỗi thao tác đều là 1 query có filter theo userId
/// nên tận dụng được index (userId, productId) và (userId, createdAt).
/// Không dùng transaction vì mỗi thao tác chỉ ghi 1 document.
/// </remarks>
public class WishlistRepository : IWishlistRepository
{
    private readonly TransactionalCollection<WishlistItem> _items;

    public WishlistRepository(MongoDbContext context)
    {
        _items = context.WishlistItems;
    }

    public async Task<WishlistItem?> GetByUserAndProductAsync(string userId, string productId)
    {
        return await _items
            .Find(i => i.UserId == userId && i.ProductId == productId)
            .FirstOrDefaultAsync();
    }

    public Task<bool> ExistsAsync(string userId, string productId)
    {
        return _items
            .Find(i => i.UserId == userId && i.ProductId == productId)
            .AnyAsync();
    }

    public Task AddAsync(WishlistItem item)
    {
        return _items.InsertOneAsync(item);
    }

    public async Task<bool> RemoveAsync(string userId, string productId)
    {
        var result = await _items.DeleteOneAsync(i => i.UserId == userId && i.ProductId == productId);
        return result.DeletedCount > 0;
    }

    public async Task<(List<WishlistItem> Items, long TotalCount)> GetPagedByUserAsync(
        string userId, int page, int pageSize)
    {
        var filter = Builders<WishlistItem>.Filter.Eq(i => i.UserId, userId);

        long totalCount = await _items.CountDocumentsAsync(filter);

        // Mới nhất trước -> dùng index (userId, createdAt desc).
        var items = await _items
            .Find(filter)
            .SortByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}
