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
    private readonly TransactionalCollection<Category> _categories;

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

    /// <summary>
    /// Lấy nhiều danh mục theo danh sách Id trong 1 query (batch lookup, tránh N+1).
    /// </summary>
    public async Task<List<Category>> GetByIdsAsync(IEnumerable<string> ids)
    {
        // Category.Id được map với BsonRepresentation(ObjectId), nên id không phải
        // ObjectId hợp lệ sẽ làm driver ném lỗi khi dựng filter. Bỏ qua các id
        // không hợp lệ thay vì làm hỏng cả truy vấn.
        var distinctIds = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Where(id => MongoDB.Bson.ObjectId.TryParse(id, out _))
            .ToList();

        if (distinctIds.Count == 0)
            return new List<Category>();

        return await _categories
            .Find(Builders<Category>.Filter.In(c => c.Id, distinctIds))
            .ToListAsync();
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
