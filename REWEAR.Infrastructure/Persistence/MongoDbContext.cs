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

            // User: 2 field (phoneNumber, birthDate) đã bị gỡ khỏi entity User ở
            // commit "delete field", nhưng các document tạo trước đó vẫn còn field.
            // MongoDB.Driver ném FormatException khi deserialize gặp field không
            // có property tương ứng, khiến mọi API đọc user (đăng nhập, profile,
            // admin list users) đều 500.
            // Unset để dữ liệu khớp với code. Idempotent: chạy lại vẫn an toàn.
            var users = _database.GetCollection<User>("Users");

            users.UpdateMany(
                Builders<User>.Filter.Or(
                    Builders<User>.Filter.Exists("phoneNumber", true),
                    Builders<User>.Filter.Exists("birthDate", true)
                ),
                Builders<User>.Update
                    .Unset("phoneNumber")
                    .Unset("birthDate")
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

            // Task 7: tra cứu timeline theo đơn, cũ nhất trước
            _database.GetCollection<OrderStatusHistory>("OrderStatusHistories").Indexes.CreateOne(
                new CreateIndexModel<OrderStatusHistory>(
                    Builders<OrderStatusHistory>.IndexKeys
                        .Ascending(h => h.OrderId)
                        .Ascending(h => h.CreatedAt),
                    new CreateIndexOptions { Name = "ix_histories_orderId_createdAt" }
                )
            );

            // Task 8: tìm phiên thanh toán mới nhất của một đơn
            _database.GetCollection<Payment>("Payments").Indexes.CreateOne(
                new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys
                        .Ascending(p => p.OrderId)
                        .Descending(p => p.CreatedAt),
                    new CreateIndexOptions { Name = "ix_payments_orderId_createdAt" }
                )
            );

            // Task 8: transactionId của PayOS phải unique. Nếu 2 giao dịch khác nhau
            // cùng nhận 1 transactionId thì đó là lỗi đồng bộ hoặc dữ liệu bị giả mạo.
            _database.GetCollection<Payment>("Payments").Indexes.CreateOne(
                new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys.Ascending(p => p.PayOsTransactionId),
                    new CreateIndexOptions<Payment>
                    {
                        Unique = true,
                        Name = "ux_payments_payosTransactionId",
                        // Chỉ áp dụng cho phiên đã có transactionId; các phiên
                        // chưa thanh toán có field này = null nên không bị xung đột.
                        PartialFilterExpression = Builders<Payment>.Filter
                            .Type(p => p.PayOsTransactionId, MongoDB.Bson.BsonType.String)
                    }
                )
            );

            // Task 8: background job quét các phiên thanh toán đã hết hạn chưa xử lý
            _database.GetCollection<Payment>("Payments").Indexes.CreateOne(
                new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys
                        .Ascending(p => p.Status)
                        .Ascending(p => p.ExpiresAt),
                    new CreateIndexOptions { Name = "ix_payments_status_expiresAt" }
                )
            );

            // Task 9: mỗi đơn chỉ có 1 bản ghi vận chuyển
            _database.GetCollection<Shipping>("Shippings").Indexes.CreateOne(
                new CreateIndexModel<Shipping>(
                    Builders<Shipping>.IndexKeys.Ascending(s => s.OrderId),
                    new CreateIndexOptions { Unique = true, Name = "ux_shippings_orderId" }
                )
            );

            // Task 9: dashboard shipper lọc đơn đang vận chuyển
            _database.GetCollection<Shipping>("Shippings").Indexes.CreateOne(
                new CreateIndexModel<Shipping>(
                    Builders<Shipping>.IndexKeys
                        .Ascending(s => s.Status)
                        .Ascending(s => s.CreatedAt),
                    new CreateIndexOptions { Name = "ix_shippings_status_createdAt" }
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

    public IMongoCollection<OrderStatusHistory> OrderStatusHistories =>
        _database.GetCollection<OrderStatusHistory>("OrderStatusHistories");

    public IMongoCollection<Payment> Payments =>
        _database.GetCollection<Payment>("Payments");

    public IMongoCollection<Shipping> Shippings =>
        _database.GetCollection<Shipping>("Shippings");

    public IMongoCollection<SourcingRequest> SourcingRequests =>
        _database.GetCollection<SourcingRequest>("SourcingRequests");
}
