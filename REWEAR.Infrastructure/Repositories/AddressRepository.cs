using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Address.
/// </summary>
public class AddressRepository : IAddressRepository
{
    private readonly MongoDbContext _context;

    public AddressRepository(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<Address?> GetByIdAsync(string id)
    {
        return await _context.Addresses
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Address>> GetByUserIdAsync(string userId)
    {
        return await _context.Addresses
            .Find(a => a.UserId == userId)
            .SortByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<Address?> GetDefaultByUserIdAsync(string userId)
    {
        return await _context.Addresses
            .Find(a => a.UserId == userId && a.IsDefault == true)
            .FirstOrDefaultAsync();
    }

    public async Task CreateAsync(Address address)
    {
        await _context.Addresses.InsertOneAsync(address);
    }

    public async Task UpdateAsync(Address address)
    {
        await _context.Addresses.ReplaceOneAsync(
            a => a.Id == address.Id,
            address
        );
    }

    public async Task DeleteAsync(string id)
    {
        await _context.Addresses.DeleteOneAsync(a => a.Id == id);
    }

    public async Task ClearDefaultForUserAsync(string userId)
    {
        var update = Builders<Address>.Update.Set(a => a.IsDefault, false);
        await _context.Addresses.UpdateManyAsync(
            a => a.UserId == userId && a.IsDefault == true,
            update
        );
    }
}
