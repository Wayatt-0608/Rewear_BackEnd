using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho Voucher (Task 10).
/// </summary>
/// <remarks>
/// Phân biệt 2 loại đếm riêng biệt:
/// - <c>UsedCount</c>: số lượt đã DÙNG THẬT (đơn đã thanh toán thành công).
/// - <c>ReservedCount</c>: số lượt đang GIỮ CHỖ cho đơn chờ thanh toán.
/// Giới hạn tổng áp dụng cho UsedCount + ReservedCount để không oversell voucher.
/// </remarks>
public interface IVoucherRepository
{
    /// <summary>
    /// Lấy voucher theo Id.
    /// </summary>
    Task<Voucher?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy voucher theo mã (Code). Tìm chính xác, case-insensitive.
    /// </summary>
    Task<Voucher?> GetByCodeAsync(string code);

    /// <summary>
    /// Lấy tất cả voucher (admin xem).
    /// </summary>
    Task<List<Voucher>> GetAllAsync();

    /// <summary>
    /// Lấy voucher theo trạng thái IsActive. null = tất cả.
    /// </summary>
    Task<List<Voucher>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Tạo voucher mới.
    /// </summary>
    Task CreateAsync(Voucher voucher);

    /// <summary>
    /// Cập nhật voucher (ghi đè toàn bộ document).
    /// </summary>
    Task UpdateAsync(Voucher voucher);

    /// <summary>
    /// Tăng số lượt đang GIỮ CHỖ lên 1 (atomic), chỉ khi còn lượt theo UsageLimit.
    /// </summary>
    /// <remarks>
    /// Filter so sánh <c>usedCount + reservedCount &lt; usageLimit</c> (khi usageLimit &gt; 0).
    /// Trả về false nếu voucher đã hết lượt → lượt giữ chỗ phải được hoàn lại.
    /// </remarks>
    Task<bool> TryIncrementReservedCountAsync(string voucherId);

    /// <summary>
    /// Ghi nhận DÙNG THẬT (atomic): tăng usedCount 1, đồng thời giảm reservedCount 1.
    /// </summary>
    /// <remarks>
    /// Chỉ thành công khi reservedCount &gt; 0 (tức là lượt này đã được giữ chỗ).
    /// Trả về false nếu không có lượt đang giữ chỗ.
    /// </remarks>
    Task<bool> TryIncrementUsedCountAsync(string voucherId);

    /// <summary>
    /// Giảm số lượt đang giữ chỗ xuống 1 (atomic, tối thiểu 0). Dùng khi giải phóng.
    /// </summary>
    Task DecrementReservedCountAsync(string voucherId);

    /// <summary>
    /// Lấy tất cả voucher đã hết hạn (ExpiryDate &lt; now) mà vẫn đang active.
    /// Dùng cho background job disable.
    /// </summary>
    Task<List<Voucher>> GetExpiredActiveVouchersAsync(int limit = 100);
}
