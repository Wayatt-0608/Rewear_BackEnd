using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Brand - implementation với MongoDB.
/// </summary>
public class BrandRepository : IBrandRepository
{
    private readonly IMongoCollection<Brand> _brands;

    public BrandRepository(MongoDbContext context)
    {
        _brands = context.Brands;
    }

    public async Task<List<Brand>> GetAllAsync()
    {
        return await _brands.Find(_ => true).ToListAsync();
    }

    public async Task<List<Brand>> GetAllAsync(bool? isActive)
    {
        if (isActive == null)
        {
            return await GetAllAsync();
        }

        return await _brands.Find(x => x.IsActive == isActive).ToListAsync();
    }

    public async Task<Brand?> GetByIdAsync(string id)
    {
        return await _brands.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Brand?> GetByNameAsync(string name)
    {
        return await _brands.Find(x => x.Name.ToLower() == name.ToLower()).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(Brand brand)
    {
        await _brands.InsertOneAsync(brand);
    }

    public async Task UpdateAsync(Brand brand)
    {
        brand.UpdatedAt = DateTime.UtcNow;
        await _brands.ReplaceOneAsync(x => x.Id == brand.Id, brand);
    }
}
