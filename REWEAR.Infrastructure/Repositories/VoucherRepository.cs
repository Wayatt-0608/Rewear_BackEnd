using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Voucher - implementation với MongoDB (Task 10).
/// </summary>
/// <remarks>
/// Phân biệt 2 bộ đếm độc lập:
/// - <c>usedCount</c>: lượt đã DÙNG THẬT (đơn đã thanh toán thành công).
/// - <c>reservedCount</c>: lượt đang GIỮ CHỖ (đơn chờ thanh toán).
/// Giới hạn tổng áp dụng cho <c>usedCount + reservedCount</c> để không oversell voucher,
/// nhưng chỉ <c>usedCount</c> mới phản ánh việc voucher đã bị tiêu thụ.
/// </remarks>
public class VoucherRepository : IVoucherRepository
{
    private readonly TransactionalCollection<Voucher> _vouchers;

    public VoucherRepository(MongoDbContext context)
    {
        _vouchers = context.Vouchers;
    }

    public async Task<Voucher?> GetByIdAsync(string id)
    {
        return await _vouchers.Find(v => v.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Voucher?> GetByCodeAsync(string code)
    {
        // Chuẩn hóa: tìm kiếm uppercase để user nhập "tet2026" hay "TET2026" đều match.
        string upperCode = code.Trim().ToUpperInvariant();
        return await _vouchers
            .Find(v => v.Code.ToUpper() == upperCode)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Voucher>> GetAllAsync()
    {
        return await _vouchers
            .Find(_ => true)
            .SortByDescending(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Voucher>> GetAllAsync(bool? isActive)
    {
        if (isActive == null)
            return await GetAllAsync();

        return await _vouchers
            .Find(v => v.IsActive == isActive)
            .SortByDescending(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task CreateAsync(Voucher voucher)
    {
        await _vouchers.InsertOneAsync(voucher);
    }

    public async Task UpdateAsync(Voucher voucher)
    {
        voucher.UpdatedAt = DateTime.UtcNow;
        await _vouchers.ReplaceOneAsync(v => v.Id == voucher.Id, voucher);
    }

    /// <summary>
    /// Tăng số lượt GIỮ CHỖ lên 1 (atomic), chỉ khi còn lượt theo UsageLimit.
    /// </summary>
    /// <remarks>
    /// Filter so sánh <c>usedCount + reservedCount &lt; usageLimit</c> (khi usageLimit &gt; 0).
    /// MongoDB.Driver không cho so sánh 2 field trực tiếp trong FilterDefinition
    /// nên dùng BsonDocument để build filter so sánh field với field.
    /// Trả về false nếu voucher đã hết lượt → lượt giữ chỗ phải được hoàn lại.
    /// </remarks>
    public async Task<bool> TryIncrementReservedCountAsync(string voucherId)
    {
        var bsonFilter = new BsonDocument
        {
            { "_id", new ObjectId(voucherId) },
            { "$or", new BsonArray
                {
                    new BsonDocument("usageLimit", 0),
                    new BsonDocument("$expr", new BsonDocument
                    {
                        { "$lt", new BsonArray
                            {
                                new BsonDocument("$add", new BsonArray { "$usedCount", "$reservedCount" }),
                                "$usageLimit"
                            }
                        }
                    })
                }
            }
        };

        var update = Builders<Voucher>.Update
            .Inc(v => v.ReservedCount, 1)
            .Set(v => v.UpdatedAt, DateTime.UtcNow);

        var result = await _vouchers.UpdateOneAsync(bsonFilter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Ghi nhận DÙNG THẬT (atomic): tăng usedCount 1, đồng thời giảm reservedCount 1.
    /// </summary>
    /// <remarks>
    /// Điều kiện: reservedCount &gt; 0 (lượt này phải đã được giữ chỗ trước đó)
    /// VÀ usedCount &lt; usageLimit (nếu có giới hạn) để không vượt UsageLimit.
    /// Trả về false nếu không thỏa → không ghi nhận dùng (idempotent khi webhook lặp).
    /// </remarks>
    public async Task<bool> TryIncrementUsedCountAsync(string voucherId)
    {
        var bsonFilter = new BsonDocument
        {
            { "_id", new ObjectId(voucherId) },
            { "reservedCount", new BsonDocument("$gt", 0) },
            { "$or", new BsonArray
                {
                    new BsonDocument("usageLimit", 0),
                    new BsonDocument("$expr", new BsonDocument
                    {
                        { "$lt", new BsonArray { "$usedCount", "$usageLimit" } }
                    })
                }
            }
        };

        var update = Builders<Voucher>.Update
            .Inc(v => v.UsedCount, 1)
            .Inc(v => v.ReservedCount, -1)
            .Set(v => v.UpdatedAt, DateTime.UtcNow);

        var result = await _vouchers.UpdateOneAsync(bsonFilter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Giảm số lượt đang giữ chỗ xuống 1 (atomic, tối thiểu 0). Dùng khi giải phóng.
    /// </summary>
    public async Task DecrementReservedCountAsync(string voucherId)
    {
        // Giảm reservedCount, tối thiểu = 0 (không cho âm).
        var filter = Builders<Voucher>.Filter.And(
            Builders<Voucher>.Filter.Eq(v => v.Id, voucherId),
            Builders<Voucher>.Filter.Gt(v => v.ReservedCount, 0)
        );

        var update = Builders<Voucher>.Update
            .Inc(v => v.ReservedCount, -1)
            .Set(v => v.UpdatedAt, DateTime.UtcNow);

        await _vouchers.UpdateOneAsync(filter, update);
    }

    public async Task<List<Voucher>> GetExpiredActiveVouchersAsync(int limit = 100)
    {
        var now = DateTime.UtcNow;
        return await _vouchers
            .Find(v => v.IsActive && v.ExpiryDate < now)
            .Limit(limit)
            .ToListAsync();
    }
}
