using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho StylingProfile - implementation với MongoDB.
/// </summary>
/// <remarks>
/// Mỗi thao tác là 1 query có filter theo <c>userId</c> nên tận dụng được unique
/// index trên <c>userId</c> của collection "StylingProfiles".
/// Không dùng transaction vì mỗi thao tác chỉ ghi 1 document.
/// </remarks>
public class StylingProfileRepository : IStylingProfileRepository
{
    private readonly TransactionalCollection<StylingProfile> _profiles;

    public StylingProfileRepository(MongoDbContext context)
    {
        _profiles = context.StylingProfiles;
    }

    public async Task<StylingProfile?> GetByUserIdAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        return await _profiles
            .Find(p => p.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public Task CreateAsync(StylingProfile profile)
    {
        return _profiles.InsertOneAsync(profile);
    }

    public async Task<bool> UpdateAsync(StylingProfile profile)
    {
        var result = await _profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteByUserIdAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var result = await _profiles.DeleteOneAsync(p => p.UserId == userId);
        return result.DeletedCount > 0;
    }
}
