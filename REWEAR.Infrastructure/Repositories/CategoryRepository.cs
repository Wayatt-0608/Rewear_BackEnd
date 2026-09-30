using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Category - implementation với MongoDB.
/// </summary>
public class CategoryRepository : ICategoryRepository
{
    private readonly IMongoCollection<Category> _categories;

    public CategoryRepository(MongoDbContext context)
    {
        _categories = context.Categories;
    }

    public async Task<List<Category>> GetAllAsync()
    {
        return await _categories.Find(_ => true).ToListAsync();
    }

    public async Task<List<Category>> GetAllAsync(bool? isActive)
    {
        if (isActive == null)
        {
            return await GetAllAsync();
        }

        return await _categories.Find(x => x.IsActive == isActive).ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(string id)
    {
        return await _categories.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Category?> GetByNameAsync(string name)
    {
        return await _categories.Find(x => x.Name.ToLower() == name.ToLower()).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(Category category)
    {
        await _categories.InsertOneAsync(category);
    }

    public async Task UpdateAsync(Category category)
    {
        category.UpdatedAt = DateTime.UtcNow;
        await _categories.ReplaceOneAsync(x => x.Id == category.Id, category);
    }
}
