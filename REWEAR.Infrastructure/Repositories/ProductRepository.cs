using MongoDB.Driver;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
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
    /// Chốt N món bằng atomic update: chỉ update được nếu sản phẩm vẫn Available,
    /// IsActive và StockQuantity >= quantity. Nếu không thỏa thì trả về 0 (không đụng DB).
    /// </summary>
    /// <remarks>
    /// Chuyển sang Reserved (không phải Sold) vì "hết hàng do đang được giữ" khác
    /// hoàn toàn với "đã bán". Nếu đánh dấu Sold ngay, hủy đơn sẽ không thể trả
    /// tồn kho lại (ReleaseStockAsync chỉ hoạt động với Available/Reserved).
    /// Trạng thái Sold chỉ được đặt khi đơn thực sự hoàn tất (CommitStockAsync, Task 7).
    /// </remarks>
    public async Task<long> TryReserveStockAsync(string productId, int quantity)
    {
        if (quantity < 1) return 0;

        var filter = Builders<Product>.Filter.And(
            Builders<Product>.Filter.Eq(x => x.Id, productId),
            Builders<Product>.Filter.Eq(x => x.Status, ProductStatus.Available),
            Builders<Product>.Filter.Eq(x => x.IsActive, true),
            Builders<Product>.Filter.Gte(x => x.StockQuantity, quantity)
        );

        var update = Builders<Product>.Update
            .Inc(x => x.StockQuantity, -quantity)
            .Set(x => x.Status, ProductStatus.Reserved)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _products.UpdateOneAsync(filter, update);
        return result.ModifiedCount;
    }

    /// <summary>
    /// Trả lại N món về kho (rollback khi checkout lỗi, hoặc khi hủy đơn).
    /// </summary>
    /// <remarks>
    /// Chấp nhận cả Available và Reserved: sau khi checkout, sản phẩm luôn ở
    /// trạng thái Reserved nên phải trả được về Available khi hủy đơn.
    /// </remarks>
    public async Task<long> ReleaseStockAsync(string productId, int quantity)
    {
        if (quantity < 1) return 0;

        var filter = Builders<Product>.Filter.And(
            Builders<Product>.Filter.Eq(x => x.Id, productId),
            Builders<Product>.Filter.In(x => x.Status, new[] { ProductStatus.Available, ProductStatus.Reserved }),
            // Không trả về kho cho sản phẩm đã bị gỡ khỏi cửa hàng.
            Builders<Product>.Filter.Eq(x => x.IsActive, true)
        );

        var update = Builders<Product>.Update
            .Inc(x => x.StockQuantity, quantity)
            .Set(x => x.Status, ProductStatus.Available)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _products.UpdateOneAsync(filter, update);
        return result.ModifiedCount;
    }

    /// <summary>
    /// Hoàn tất bán N món (gọi khi đơn chuyển Confirmed/Delivered - Task 7).
    /// Tồn kho đã bị trừ lúc Reserve nên bước này chỉ chốt lại trạng thái:
    /// hết tồn thì Sold (không ai mua được nữa), còn hàng thì Available
    /// (các món chưa bán của cùng listing vẫn mua được).
    /// </summary>
    public async Task<long> CommitStockAsync(string productId, int quantity)
    {
        if (quantity < 1) return 0;

        // Hết tồn -> Sold. Điều kiện Status = Reserved để không ghi đè
        // trạng thái của một giao dịch khác đang xử lý.
        var soldResult = await _products.UpdateOneAsync(
            Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(x => x.Id, productId),
                Builders<Product>.Filter.Eq(x => x.Status, ProductStatus.Reserved),
                Builders<Product>.Filter.Lte(x => x.StockQuantity, 0)
            ),
            Builders<Product>.Update
                .Set(x => x.Status, ProductStatus.Sold)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
        );

        // Còn tồn -> Available (listing nhiều món vẫn bán tiếp được).
        var availableResult = await _products.UpdateOneAsync(
            Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(x => x.Id, productId),
                Builders<Product>.Filter.Eq(x => x.Status, ProductStatus.Reserved),
                Builders<Product>.Filter.Gt(x => x.StockQuantity, 0)
            ),
            Builders<Product>.Update
                .Set(x => x.Status, ProductStatus.Available)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
        );

        return soldResult.ModifiedCount + availableResult.ModifiedCount;
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
