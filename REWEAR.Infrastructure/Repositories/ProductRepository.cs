using MongoDB.Driver;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Product - implementation với MongoDB.
/// </summary>
public class ProductRepository : IProductRepository
{
    private readonly IMongoCollection<Product> _products;

    public ProductRepository(MongoDbContext context)
    {
        _products = context.Products;
    }

    public async Task<List<Product>> GetAllAsync()
    {
        return await _products.Find(_ => true).ToListAsync();
    }

    public async Task<List<Product>> GetAllAsync(bool? isActive)
    {
        if (isActive == null)
        {
            return await GetAllAsync();
        }

        return await _products.Find(x => x.IsActive == isActive).ToListAsync();
    }

    public async Task<List<Product>> GetAllAsync(ProductQueryParameters query)
    {
        // Build filter definitions
        var filters = new List<FilterDefinition<Product>>();

        // IsActive filter
        if (query.IsActive.HasValue)
        {
            filters.Add(Builders<Product>.Filter.Eq(x => x.IsActive, query.IsActive.Value));
        }

        // BrandId filter
        if (!string.IsNullOrWhiteSpace(query.BrandId))
        {
            filters.Add(Builders<Product>.Filter.Eq(x => x.BrandId, query.BrandId));
        }

        // CategoryId filter
        if (!string.IsNullOrWhiteSpace(query.CategoryId))
        {
            filters.Add(Builders<Product>.Filter.Eq(x => x.CategoryId, query.CategoryId));
        }

        // Condition filter
        if (query.Condition.HasValue)
        {
            filters.Add(Builders<Product>.Filter.Eq(x => x.Condition, query.Condition.Value));
        }

        // Status filter
        if (query.Status.HasValue)
        {
            filters.Add(Builders<Product>.Filter.Eq(x => x.Status, query.Status.Value));
        }

        // Price range filter
        if (query.MinPrice.HasValue)
        {
            filters.Add(Builders<Product>.Filter.Gte(x => x.Price, query.MinPrice.Value));
        }

        if (query.MaxPrice.HasValue)
        {
            filters.Add(Builders<Product>.Filter.Lte(x => x.Price, query.MaxPrice.Value));
        }

        // Search filter (Title and Description, case-insensitive)
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Escape regex special characters to prevent injection
            string escapedSearch = EscapeRegex(query.Search);
            
            var titleFilter = Builders<Product>.Filter.Regex(
                x => x.Title, 
                new MongoDB.Bson.BsonRegularExpression(escapedSearch, "i"));
            
            var descFilter = Builders<Product>.Filter.Regex(
                x => x.Description, 
                new MongoDB.Bson.BsonRegularExpression(escapedSearch, "i"));
            
            // OR between Title and Description
            filters.Add(Builders<Product>.Filter.Or(titleFilter, descFilter));
        }

        // Combine all filters with AND
        FilterDefinition<Product> combinedFilter;
        if (filters.Count == 0)
        {
            combinedFilter = Builders<Product>.Filter.Empty;
        }
        else
        {
            combinedFilter = Builders<Product>.Filter.And(filters);
        }

        return await _products.Find(combinedFilter).ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(string id)
    {
        return await _products.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Product?> GetBySlugAsync(string slug)
    {
        return await _products.Find(x => x.Slug == slug).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(Product product)
    {
        await _products.InsertOneAsync(product);
    }

    public async Task UpdateAsync(Product product)
    {
        product.UpdatedAt = DateTime.UtcNow;
        await _products.ReplaceOneAsync(x => x.Id == product.Id, product);
    }

    /// <summary>
    /// Escape special regex characters to prevent regex injection.
    /// </summary>
    private static string EscapeRegex(string input)
    {
        // Escape special regex characters: . $ ^ { [ ( | ) * + ? \
        return System.Text.RegularExpressions.Regex.Escape(input);
    }
}
