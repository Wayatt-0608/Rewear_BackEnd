using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho SourcingRequest - implementation với MongoDB.
/// </summary>
public class SourcingRepository : ISourcingRepository
{
    private readonly IMongoCollection<SourcingRequest> _sourcingRequests;

    public SourcingRepository(MongoDbContext context)
    {
        _sourcingRequests = context.SourcingRequests;
    }

    /// <inheritdoc />
    public async Task<SourcingRequest?> GetByIdAsync(string id)
    {
        return await _sourcingRequests.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<List<SourcingRequest>> GetByUserIdAsync(string userId)
    {
        return await _sourcingRequests
            .Find(x => x.UserId == userId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<SourcingRequest>> GetAllAsync(SourcingStatus? status = null)
    {
        if (status == null)
        {
            return await _sourcingRequests
                .Find(_ => true)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        return await _sourcingRequests
            .Find(x => x.Status == status.Value)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task CreateAsync(SourcingRequest sourcingRequest)
    {
        await _sourcingRequests.InsertOneAsync(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(SourcingRequest sourcingRequest)
    {
        sourcingRequest.UpdatedAt = DateTime.UtcNow;
        var result = await _sourcingRequests.ReplaceOneAsync(
            x => x.Id == sourcingRequest.Id,
            sourcingRequest);

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> IsImageUrlInUseAsync(string imageUrl)
    {
        // Find any sourcing request that uses this image URL
        var filter = Builders<SourcingRequest>.Filter.ElemMatch(
            x => x.ImageUrls,
            url => url == imageUrl);

        var count = await _sourcingRequests.CountDocumentsAsync(filter);
        return count > 0;
    }
}
