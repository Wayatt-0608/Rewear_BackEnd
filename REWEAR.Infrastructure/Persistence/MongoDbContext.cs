using MongoDB.Driver;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(MongoDbSettings settings)
    {
        var client = new MongoClient(settings.ConnectionString);
        _database = client.GetDatabase(settings.DatabaseName);

        EnsureIndexes();
        MigrateLegacyDocuments();
    }

    /// <summary>
    /// Backfill các field mới thêm sau cho document đã tồn tại trước đó.
    /// MongoDB không tự gán default khi document thiếu field, nên sản phẩm tạo
    /// trước khi thêm StockQuantity sẽ deserialize thành 0 và bị coi là hết hàng.
    /// Các hàm dưới đây idempotent: chạy lại nhiều lần cũng không gây tác dụng phụ.
    /// </summary>
    private void MigrateLegacyDocuments()
    {
        try
        {
            // Product.stockQuantity: coi sản phẩm cũ (không có field) là còn 1 món,
            // đúng với giả định "1 Product = 1 món vật lý" trước khi có tính năng này.
            // Chỉ set khi field thực sự không tồn tại (Exists == false), KHÔNG đụng
            // các sản phẩm đã bán hết (stockQuantity = 0) hoặc còn tồn (stockQuantity > 0).
            _database.GetCollection<Product>("Products").UpdateMany(
                Builders<Product>.Filter.Exists("stockQuantity", false),
                Builders<Product>.Update.Set("stockQuantity", 1)
            );
        }
        catch (MongoConnectionException)
        {
            // Không kết nối được DB -> bỏ qua, không chặn app khởi động.
        }
        catch (MongoCommandException)
        {
            // Không đủ quyền ghi -> bỏ qua.
        }
    }

    /// <summary>
    /// Tạo các index cần thiết cho các collection.
    /// - Carts: unique theo userId (1 user chỉ có 1 giỏ) + TTL index trên expiresAt
    ///   để MongoDB tự động xóa giỏ hàng bị bỏ quên.
    /// </summary>
    private void EnsureIndexes()
    {
        try
        {
            // 1 user chỉ có 1 giỏ hàng
            _database.GetCollection<Cart>("Carts").Indexes.CreateOne(
                new CreateIndexModel<Cart>(
                    Builders<Cart>.IndexKeys.Ascending(c => c.UserId),
                    new CreateIndexOptions { Unique = true, Name = "ux_carts_userId" }
                )
            );

            // TTL: tự động xóa giỏ hàng sau khi hết hạn
            _database.GetCollection<Cart>("Carts").Indexes.CreateOne(
                new CreateIndexModel<Cart>(
                    Builders<Cart>.IndexKeys.Ascending(c => c.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_carts_expiresAt" }
                )
            );

            // Tối ưu truy vấn lấy giỏ theo userId (phục vụ RoleChecking/log)
            _database.GetCollection<Address>("Addresses").Indexes.CreateOne(
                new CreateIndexModel<Address>(
                    Builders<Address>.IndexKeys.Ascending(a => a.UserId),
                    new CreateIndexOptions { Name = "ix_addresses_userId" }
                )
            );

            // Orders: tra cứu lịch sử đơn hàng theo user (lần gần nhất trước)
            _database.GetCollection<Order>("Orders").Indexes.CreateOne(
                new CreateIndexModel<Order>(
                    Builders<Order>.IndexKeys
                        .Ascending(o => o.UserId)
                        .Descending(o => o.CreatedAt),
                    new CreateIndexOptions { Name = "ix_orders_userId_createdAt" }
                )
            );

            // Orders: orderCode là mã hiển thị cho user, phải unique
            _database.GetCollection<Order>("Orders").Indexes.CreateOne(
                new CreateIndexModel<Order>(
                    Builders<Order>.IndexKeys.Ascending(o => o.OrderCode),
                    new CreateIndexOptions { Unique = true, Name = "ux_orders_orderCode" }
                )
            );

            // Orders: lọc đơn theo trạng thái (admin/seller xử lý vận chuyển)
            _database.GetCollection<Order>("Orders").Indexes.CreateOne(
                new CreateIndexModel<Order>(
                    Builders<Order>.IndexKeys
                        .Ascending(o => o.Status)
                        .Ascending(o => o.CreatedAt),
                    new CreateIndexOptions { Name = "ix_orders_status_createdAt" }
                )
            );
        }
        catch (MongoWriteException)
        {
            // Index đã tồn tại hoặc conflict -> bỏ qua, không chặn app khởi động
        }
        catch (MongoCommandException)
        {
            // Không đủ quyền tạo index -> bỏ qua
        }
    }

    public IMongoCollection<Product> Products =>
        _database.GetCollection<Product>("Products");

    public IMongoCollection<User> Users =>
        _database.GetCollection<User>("Users");

    public IMongoCollection<Brand> Brands =>
        _database.GetCollection<Brand>("Brands");

    public IMongoCollection<Category> Categories =>
        _database.GetCollection<Category>("Categories");

    public IMongoCollection<Address> Addresses =>
        _database.GetCollection<Address>("Addresses");

    public IMongoCollection<Cart> Carts =>
        _database.GetCollection<Cart>("Carts");

    public IMongoCollection<Order> Orders =>
        _database.GetCollection<Order>("Orders");

    public IMongoCollection<SourcingRequest> SourcingRequests =>
        _database.GetCollection<SourcingRequest>("SourcingRequests");
}
