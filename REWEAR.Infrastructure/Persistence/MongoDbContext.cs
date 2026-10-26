using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly IMongoClient _client;
    private readonly AsyncLocal<IClientSessionHandle?> _session = new();

    public async Task<T> ExecuteTransactionAsync<T>(Func<Task<T>> operation)
    {
        if (_session.Value != null) return await operation();
        using var session = await _client.StartSessionAsync();
        return await session.WithTransactionAsync(async (_, cancellationToken) =>
        {
            _session.Value = session;
            try { return await operation(); }
            finally { _session.Value = null; }
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority));
    }

    public TransactionalCollection<T> Collection<T>(string name) => new(_database.GetCollection<T>(name), () => _session.Value);

    public MongoDbContext(MongoDbSettings settings)
    {
        var client = _client = new MongoClient(settings.ConnectionString);
        _database = client.GetDatabase(settings.DatabaseName);

        EnsureIndexes();
        // Financial and user documents are not rewritten automatically at startup.
    }

    /// <summary>
    /// Retains baseline indexes only. No startup document cleanup or fabricated stock/financial data.
    /// New commerce indexes are installed explicitly by CommerceSchemaMigration.
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
            // Index chỉ áp dụng khi field là chuỗi (đã có transactionId), tránh
            // xung đột giữa các phiên chưa thanh toán (PayOsTransactionId = null).
            var paymentsColl = _database.GetCollection<Payment>("Payments");

            // Never drop existing indexes on startup. Use the versioned migration.

            paymentsColl.Indexes.CreateOne(
                new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys.Ascending(p => p.PayOsTransactionId),
                    new CreateIndexOptions<Payment>
                    {
                        Unique = true,
                        Name = "ux_payments_payosTransactionId",
                        // Chỉ áp dụng khi PayOsTransactionId là chuỗi và không null.
                        // Phiên chưa thanh toán có field null/"" sẽ không bị xung đột.
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

            // ====== TASK 10: VOUCHER ======
            var vouchersColl = _database.GetCollection<Voucher>("Vouchers");

            // Mã voucher phải unique (case-insensitive sẽ xử lý ở Service).
            vouchersColl.Indexes.CreateOne(
                new CreateIndexModel<Voucher>(
                    Builders<Voucher>.IndexKeys.Ascending(v => v.Code),
                    new CreateIndexOptions { Unique = true, Name = "ux_vouchers_code" }
                )
            );

            // Lọc voucher theo trạng thái active (admin list).
            vouchersColl.Indexes.CreateOne(
                new CreateIndexModel<Voucher>(
                    Builders<Voucher>.IndexKeys
                        .Ascending(v => v.IsActive)
                        .Descending(v => v.CreatedAt),
                    new CreateIndexOptions { Name = "ix_vouchers_isActive_createdAt" }
                )
            );

            // Background job: tìm voucher hết hạn.
            vouchersColl.Indexes.CreateOne(
                new CreateIndexModel<Voucher>(
                    Builders<Voucher>.IndexKeys
                        .Ascending(v => v.IsActive)
                        .Ascending(v => v.ExpiryDate),
                    new CreateIndexOptions { Name = "ix_vouchers_isActive_expiryDate" }
                )
            );

            var userVouchersColl = _database.GetCollection<UserVoucher>("UserVouchers");

            // Kho voucher của 1 user.
            userVouchersColl.Indexes.CreateOne(
                new CreateIndexModel<UserVoucher>(
                    Builders<UserVoucher>.IndexKeys
                        .Ascending(uv => uv.UserId)
                        .Descending(uv => uv.GrantedAt),
                    new CreateIndexOptions { Name = "ix_userVouchers_userId_grantedAt" }
                )
            );

            // Tra cứu nhanh UserVoucher theo (user, voucher) - 1 user chỉ có 1 record / voucher.
            userVouchersColl.Indexes.CreateOne(
                new CreateIndexModel<UserVoucher>(
                    Builders<UserVoucher>.IndexKeys
                        .Ascending(uv => uv.UserId)
                        .Ascending(uv => uv.VoucherId),
                    new CreateIndexOptions { Unique = true, Name = "ux_userVouchers_userId_voucherId" }
                )
            );

            // Stats: lấy tất cả UserVoucher của 1 voucher.
            userVouchersColl.Indexes.CreateOne(
                new CreateIndexModel<UserVoucher>(
                    Builders<UserVoucher>.IndexKeys.Ascending(uv => uv.VoucherId),
                    new CreateIndexOptions { Name = "ix_userVouchers_voucherId" }
                )
            );

            // ====== WISHLIST =====
            var wishlistItemsColl = _database.GetCollection<WishlistItem>("WishlistItems");

            // 1 user chỉ lưu 1 product 1 lần.
            // Unique compound index là hàng rào chống duplicate khi 2 request thêm chạy SONG SONG
            // (request thua nhận MongoWriteException code 11000 và được service xử lý idempotent).
            wishlistItemsColl.Indexes.CreateOne(
                new CreateIndexModel<WishlistItem>(
                    Builders<WishlistItem>.IndexKeys
                        .Ascending(i => i.UserId)
                        .Ascending(i => i.ProductId),
                    new CreateIndexOptions { Unique = true, Name = "ux_wishlistItems_userId_productId" }
                )
            );

            // Truy vấn danh sách wishlist của 1 user, sort mới nhất trước.
            wishlistItemsColl.Indexes.CreateOne(
                new CreateIndexModel<WishlistItem>(
                    Builders<WishlistItem>.IndexKeys
                        .Ascending(i => i.UserId)
                        .Descending(i => i.CreatedAt),
                    new CreateIndexOptions { Name = "ix_wishlistItems_userId_createdAt" }
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

    public TransactionalCollection<Product> Products =>
        Collection<Product>("Products");

    public TransactionalCollection<User> Users =>
        Collection<User>("Users");

    public TransactionalCollection<Brand> Brands =>
        Collection<Brand>("Brands");

    public TransactionalCollection<Category> Categories =>
        Collection<Category>("Categories");

    public TransactionalCollection<Address> Addresses =>
        Collection<Address>("Addresses");

    public TransactionalCollection<Cart> Carts =>
        Collection<Cart>("Carts");

    public TransactionalCollection<Order> Orders =>
        Collection<Order>("Orders");

    public TransactionalCollection<OrderStatusHistory> OrderStatusHistories =>
        Collection<OrderStatusHistory>("OrderStatusHistories");

    public TransactionalCollection<Payment> Payments =>
        Collection<Payment>("Payments");

    public TransactionalCollection<Shipping> Shippings =>
        Collection<Shipping>("Shippings");

    public TransactionalCollection<SourcingRequest> SourcingRequests =>
        Collection<SourcingRequest>("SourcingRequests");

    // ====== TASK 10: VOUCHER ======
    public TransactionalCollection<Voucher> Vouchers =>
        Collection<Voucher>("Vouchers");

    public TransactionalCollection<UserVoucher> UserVouchers =>
        Collection<UserVoucher>("UserVouchers");

    // ====== WISHLIST =====
    public TransactionalCollection<WishlistItem> WishlistItems =>
        Collection<WishlistItem>("WishlistItems");
}
