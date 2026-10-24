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
    public async Task<bool> HasActiveAllocationAsync(string productId) => await _reservations
        .CountDocumentsAsync(r => r.ProductId == productId && (r.State == "Reserved" || r.State == "Paid")) > 0;
    private readonly TransactionalCollection<Product> _products;
    private readonly TransactionalCollection<StockReservation> _reservations;
    private readonly MongoDbContext _context;

    public ProductRepository(MongoDbContext context)
    {
        _context = context;
        _products = context.Products;
        _reservations = context.Collection<StockReservation>("StockReservations");
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
        var version = product.Version;
        product.Version++;
        var filter = Builders<Product>.Filter.Eq(p => p.Id, product.Id) & (version == 0
            ? Builders<Product>.Filter.Or(Builders<Product>.Filter.Eq(p => p.Version, 0), Builders<Product>.Filter.Exists(p => p.Version, false))
            : Builders<Product>.Filter.Eq(p => p.Version, version));
        if ((await _products.ReplaceOneAsync(filter, product)).MatchedCount == 0)
            throw new InvalidOperationException("Sản phẩm đã thay đổi. Vui lòng tải lại trước khi lưu.");
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

    public async Task<long> TryReserveStockAsync(string productId, int quantity, string orderId, DateTime expiresAt)
    {
        if (quantity < 1 || string.IsNullOrWhiteSpace(orderId)) return 0;
        var key = orderId + ":" + productId;
        var existing = await _reservations.Find(r => r.Id == key).FirstOrDefaultAsync();
        if (existing != null) return existing.State == "Reserved" && existing.Quantity == quantity ? 1 : 0;
        var product = await GetByIdAsync(productId);
        if (product == null || !product.IsActive || product.Status != ProductStatus.Available || product.StockQuantity < quantity) return 0;
        bool unique = product.IsUnique ?? product.StockQuantity <= 1;
        if (unique && quantity != 1) return 0;
        var updated = await _products.UpdateOneAsync(p => p.Id == productId && p.Status == ProductStatus.Available && p.IsActive && p.StockQuantity >= quantity,
            Builders<Product>.Update.Inc(p => p.StockQuantity, -quantity).Set(p => p.IsUnique, unique)
                .Set(p => p.Status, product.StockQuantity > quantity ? ProductStatus.Available : ProductStatus.Reserved).Set(p => p.UpdatedAt, DateTime.UtcNow).Inc(p => p.Version, 1));
        if (updated.ModifiedCount == 0) return 0;
        await _reservations.InsertOneAsync(new StockReservation { Id = key, OrderId = orderId, ProductId = productId, Quantity = quantity, ExpiresAt = expiresAt });
        await MoveRetailInventory(productId, "Reserved", orderId);
        return 1;
    }

    public Task<long> ReleaseStockAsync(string productId, int quantity, string orderId) => RestoreAsync(productId, quantity, orderId, "Reserved");
    public Task<long> RestorePaidStockAsync(string productId, int quantity, string orderId) => RestoreAsync(productId, quantity, orderId, "Paid");

    private async Task<long> RestoreAsync(string productId, int quantity, string orderId, string fromState)
    {
        if (quantity < 1 || string.IsNullOrWhiteSpace(orderId)) return 0;
        var key = orderId + ":" + productId;
        var changed = await _reservations.UpdateOneAsync(r => r.Id == key && r.Quantity == quantity && r.State == fromState,
            Builders<StockReservation>.Update.Set(r => r.State, "Released").Set(r => r.UpdatedAt, DateTime.UtcNow));
        if (changed.ModifiedCount == 0) return 0;
        var product = await GetByIdAsync(productId) ?? throw new InvalidOperationException("Không tìm thấy sản phẩm để trả kho.");
        // Publication visibility is independent; an inactive item still receives its stock back.
        await _products.UpdateOneAsync(p => p.Id == productId,
            Builders<Product>.Update.Inc(p => p.StockQuantity, quantity).Set(p => p.Status, ProductStatus.Available).Set(p => p.UpdatedAt, DateTime.UtcNow).Inc(p => p.Version, 1));
        await MoveRetailInventory(productId, "ReadyRetail", orderId);
        return 1;
    }

    public async Task<long> CommitStockAsync(string productId, int quantity, string orderId)
    {
        var key = orderId + ":" + productId;
        var changed = await _reservations.UpdateOneAsync(r => r.Id == key && r.Quantity == quantity && r.State == "Reserved",
            Builders<StockReservation>.Update.Set(r => r.State, "Paid").Set(r => r.UpdatedAt, DateTime.UtcNow));
        if (changed.ModifiedCount == 0) return 0;
        var product = await GetByIdAsync(productId) ?? throw new InvalidOperationException("Không tìm thấy sản phẩm đã giữ.");
        await _products.UpdateOneAsync(p => p.Id == productId,
            Builders<Product>.Update.Set(p => p.Status, product.StockQuantity > 0 ? ProductStatus.Available : ProductStatus.Sold).Set(p => p.UpdatedAt, DateTime.UtcNow).Inc(p => p.Version, 1));
        await MoveRetailInventory(productId, "Sold", orderId);
        return 1;
    }

    private async Task MoveRetailInventory(string productId, string target, string orderId)
    {
        var collection = _context.Collection<InventoryItem>("InventoryItems");
        var rows = await collection.Find(i => i.ProductId == productId && i.State != "Consumed").ToListAsync();
        foreach (var row in rows)
        {
            var from = row.State;
            bool allowed = target switch
            {
                "Reserved" => from == "ReadyRetail",
                "Sold" => from == "Reserved",
                "ReadyRetail" => from is "Reserved" or "Sold" or "ReturnAwaitingRefund",
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Trạng thái kho vòng đời không khớp phân bổ đơn hàng; cần đối soát.");
            row.State = target; row.Version++; row.UpdatedAt = DateTime.UtcNow;
            await collection.ReplaceOneAsync(i => i.Id == row.Id, row);
            await _context.Collection<InventoryMovement>("InventoryMovements").InsertOneAsync(new()
            { InventoryId = row.Id, FromState = from, ToState = target, ActorId = "System", RelatedId = orderId, Reason = "Đồng bộ phân bổ tồn kho theo đơn hàng." });
        }
    }

    /// <summary>
    /// Escape special regex characters to prevent regex injection.
    /// </summary>
    private static string EscapeRegex(string input)
    {
        // Escape special regex characters: . $ ^ { [ ( | ) * + ? \
        return System.Text.RegularExpressions.Regex.Escape(input);
    }

    /// <inheritdoc />
    public async Task<bool> IsImageUrlInUseByOtherProductAsync(string imageUrl, string? excludeProductId = null)
    {
        // Build filter to find products that have this image URL
        var filter = Builders<Product>.Filter.ElemMatch(
            x => x.ImageUrls,
            url => url == imageUrl);

        // Exclude the current product if specified
        if (!string.IsNullOrEmpty(excludeProductId))
        {
            filter = Builders<Product>.Filter.And(
                filter,
                Builders<Product>.Filter.Ne(x => x.Id, excludeProductId)
            );
        }

        // Count products using this URL (excluding the current one)
        var count = await _products.CountDocumentsAsync(filter);
        return count > 0;
    }
}
