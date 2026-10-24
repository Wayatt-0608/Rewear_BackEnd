using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho UserVoucher - implementation với MongoDB (Task 10).
/// </summary>
/// <remarks>
/// Mọi chuyển trạng thái đều dùng atomic update có điều kiện (compare-and-set):
/// filter mô tả trạng thái ĐƯỢC PHÉP chuyển đi, update mô tả trạng thái MỚI.
/// Nếu 2 luồng chạy song song, chỉ 1 luồng match filter → chỉ 1 luồng thành công.
/// Đây là cơ chế thay thế transaction cho MongoDB standalone (không đảm bảo multi-doc ACID).
/// </remarks>
public class UserVoucherRepository : IUserVoucherRepository
{
    private readonly TransactionalCollection<UserVoucher> _userVouchers;

    public UserVoucherRepository(MongoDbContext context)
    {
        _userVouchers = context.UserVouchers;
    }

    public async Task<UserVoucher?> GetByIdAsync(string id)
    {
        return await _userVouchers.Find(uv => uv.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<UserVoucher>> GetByUserIdAsync(string userId)
    {
        return await _userVouchers
            .Find(uv => uv.UserId == userId)
            .SortByDescending(uv => uv.GrantedAt)
            .ToListAsync();
    }

    public async Task<UserVoucher?> GetByUserAndVoucherAsync(string userId, string voucherId)
    {
        return await _userVouchers
            .Find(uv => uv.UserId == userId && uv.VoucherId == voucherId)
            .FirstOrDefaultAsync();
    }

    public async Task CreateAsync(UserVoucher userVoucher)
    {
        await _userVouchers.InsertOneAsync(userVoucher);
    }

    public async Task UpdateAsync(UserVoucher userVoucher)
    {
        await _userVouchers.ReplaceOneAsync(uv => uv.Id == userVoucher.Id, userVoucher);
    }

    /// <summary>
    /// Available → Reserved (atomic). Giữ chỗ voucher cho 1 đơn.
    /// </summary>
    /// <remarks>
    /// Filter chỉ match khi lượt voucher đang Available (chưa giữ chỗ, chưa dùng).
    /// Ngoài ra còn check UsageLimitPerUser bằng <c>usageCount &lt; usageLimitPerUser</c>
    /// (bỏ qua khi usageLimitPerUser = 0 = không giới hạn).
    /// Vì <c>usageCount</c> chỉ tăng khi DÙNG THẬT, điều kiện này chặn user vượt
    /// giới hạn dùng mỗi user.
    /// </remarks>
    public async Task<bool> TryReserveAsync(
        string userId, string voucherId, string orderId, int usageLimitPerUser)
    {
        var filters = new List<FilterDefinition<UserVoucher>>
        {
            Builders<UserVoucher>.Filter.Eq(uv => uv.UserId, userId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.VoucherId, voucherId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.Status, UserVoucherStatus.Available)
        };

        // usageLimitPerUser = 0 => không giới hạn số lần dùng của user này.
        if (usageLimitPerUser > 0)
        {
            filters.Add(Builders<UserVoucher>.Filter.Lt(uv => uv.UsageCount, usageLimitPerUser));
        }

        var filter = Builders<UserVoucher>.Filter.And(filters);

        var update = Builders<UserVoucher>.Update
            .Set(uv => uv.Status, UserVoucherStatus.Reserved)
            .Set(uv => uv.OrderId, orderId)
            .Set(uv => uv.ReservedAt, DateTime.UtcNow)
            .Set(uv => uv.UsedAt, null);

        var result = await _userVouchers.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Reserved → Available (atomic). Giải phóng voucher khi đơn không còn dùng nó.
    /// </summary>
    /// <remarks>
    /// Filter match chỉ khi lượt voucher đang Reserved VÀ đang gắn với đúng orderId
    /// → không bao giờ giải phóng nhầm voucher của đơn khác, và gọi lại nhiều lần
    /// chỉ lần đầu thành công (lần sau không match vì status đã là Available).
    /// </remarks>
    public async Task<bool> TryReleaseAsync(string userId, string voucherId, string orderId)
    {
        var filter = Builders<UserVoucher>.Filter.And(
            Builders<UserVoucher>.Filter.Eq(uv => uv.UserId, userId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.VoucherId, voucherId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.Status, UserVoucherStatus.Reserved),
            Builders<UserVoucher>.Filter.Eq(uv => uv.OrderId, orderId)
        );

        var update = Builders<UserVoucher>.Update
            .Set(uv => uv.Status, UserVoucherStatus.Available)
            .Set(uv => uv.OrderId, null)
            .Set(uv => uv.ReservedAt, null)
            .Set(uv => uv.UsedAt, null);

        var result = await _userVouchers.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Reserved → Used (atomic), tăng UsageCount của user.
    /// </summary>
    /// <remarks>
    /// Filter match chỉ khi lượt voucher đang Reserved VÀ gắn với đúng orderId
    /// → webhook lặp gọi nhiều lần chỉ ghi nhận 1 lần (lần 2 không match vì
    /// status đã là Used).
    ///
    /// Sau khi dùng thật: nếu user đã dùng đủ usageLimitPerUser lượt thì lượt
    /// voucher chuyển hẳn sang Used (hạn dùng); nếu còn lượt thì quay lại Available
    /// để user dùng tiếp (UsageLimitPerUser &gt; 1).
    /// </remarks>
    public async Task<bool> TryConsumeAsync(
        string userId, string voucherId, string orderId, int usageLimitPerUser)
    {
        var filter = Builders<UserVoucher>.Filter.And(
            Builders<UserVoucher>.Filter.Eq(uv => uv.UserId, userId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.VoucherId, voucherId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.Status, UserVoucherStatus.Reserved),
            Builders<UserVoucher>.Filter.Eq(uv => uv.OrderId, orderId)
        );

        var update = Builders<UserVoucher>.Update
            .Set(uv => uv.Status, UserVoucherStatus.Used)
            .Set(uv => uv.UsedAt, DateTime.UtcNow)
            .Set(uv => uv.ReservedAt, null)
            .Inc(uv => uv.UsageCount, 1);

        // FindOneAndUpdate là atomic và trả về document SAU khi update -> biết được
        // UsageCount mới để quyết định có mở lại lượt voucher hay không.
        var updated = await _userVouchers.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<UserVoucher>
            {
                ReturnDocument = ReturnDocument.After
            });

        if (updated == null)
            return false;

        // usageLimitPerUser = 0 => không giới hạn, luôn mở lại.
        // usageLimitPerUser > 0 và còn lượt => mở lại lượt voucher cho lần dùng sau.
        bool stillHasQuota = usageLimitPerUser <= 0 || updated.UsageCount < usageLimitPerUser;

        if (stillHasQuota)
        {
            // Mở lại lượt voucher: Available, bỏ gắn đơn, nhưng GIỮ UsageCount
            // (để lần reserve sau vẫn bị chặn khi đã vượt giới hạn mỗi user).
            await _userVouchers.UpdateOneAsync(
                Builders<UserVoucher>.Filter.And(
                    Builders<UserVoucher>.Filter.Eq(uv => uv.Id, updated.Id),
                    Builders<UserVoucher>.Filter.Eq(uv => uv.Status, UserVoucherStatus.Used),
                    Builders<UserVoucher>.Filter.Eq(uv => uv.OrderId, orderId)
                ),
                Builders<UserVoucher>.Update
                    .Set(uv => uv.Status, UserVoucherStatus.Available)
                    .Set(uv => uv.OrderId, null)
                    .Set(uv => uv.UsedAt, null)
            );
        }

        return true;
    }

    public async Task<int> CountByVoucherAsync(string voucherId)
    {
        long count = await _userVouchers.CountDocumentsAsync(uv => uv.VoucherId == voucherId);
        return (int)count;
    }

    /// <summary>
    /// Đếm số UserVoucher đã dùng thật ít nhất 1 lần.
    /// </summary>
    /// <remarks>
    /// Dùng <c>usageCount &gt; 0</c> thay vì <c>status == Used</c> vì khi
    /// UsageLimitPerUser &gt; 1, lượt voucher quay lại Available sau khi dùng
    /// nhưng UsageCount vẫn ghi nhận đã dùng.
    /// </remarks>
    public async Task<int> CountUsedByVoucherAsync(string voucherId)
    {
        long count = await _userVouchers.CountDocumentsAsync(
            uv => uv.VoucherId == voucherId && uv.UsageCount > 0);
        return (int)count;
    }

    /// <summary>
    /// Giảm UsageCount của user xuống 1 (atomic, tối thiểu 0).
    /// </summary>
    /// <remarks>
    /// Dùng để hoàn tát khi ghi nhận dùng thật thất bại ở phía voucher, để
    /// UsageCount của user không bị tính nhầm lượt đã dùng.
    /// </remarks>
    public async Task<bool> DecrementUsageCountAsync(string userId, string voucherId)
    {
        var filter = Builders<UserVoucher>.Filter.And(
            Builders<UserVoucher>.Filter.Eq(uv => uv.UserId, userId),
            Builders<UserVoucher>.Filter.Eq(uv => uv.VoucherId, voucherId),
            Builders<UserVoucher>.Filter.Gt(uv => uv.UsageCount, 0)
        );

        var update = Builders<UserVoucher>.Update.Inc(uv => uv.UsageCount, -1);

        var result = await _userVouchers.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Đếm số UserVoucher đang giữ chỗ (chưa thanh toán).
    /// </summary>
    public async Task<int> CountReservedByVoucherAsync(string voucherId)
    {
        long count = await _userVouchers.CountDocumentsAsync(
            uv => uv.VoucherId == voucherId && uv.Status == UserVoucherStatus.Reserved);
        return (int)count;
    }

    public async Task<List<UserVoucher>> GetByVoucherIdAsync(string voucherId)
    {
        return await _userVouchers
            .Find(uv => uv.VoucherId == voucherId)
            .ToListAsync();
    }

    public async Task CreateManyAsync(IEnumerable<UserVoucher> userVouchers)
    {
        var list = userVouchers.ToList();
        if (list.Count == 0)
            return;

        // MongoDB.Driver hỗ trợ InsertManyAsync - 1 round-trip cho cả batch.
        await _userVouchers.InsertManyAsync(list, new InsertManyOptions
        {
            IsOrdered = false // Tiếp tục insert kể cả khi 1 cái fail (idempotent retry)
        });
    }
}
