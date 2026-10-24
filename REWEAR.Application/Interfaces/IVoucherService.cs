using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service xử lý business logic cho Voucher (Task 10).
/// </summary>
public interface IVoucherService
{
    // ====== CRUD (Admin) ======

    /// <summary>
    /// Tạo voucher mới. Validate: code unique, ngày hợp lệ, value hợp lệ.
    /// </summary>
    Task<VoucherResponse> CreateAsync(CreateVoucherRequest request, string adminId);

    /// <summary>
    /// Cập nhật voucher.
    /// </summary>
    Task<VoucherResponse?> UpdateAsync(string id, UpdateVoucherRequest request);

    /// <summary>
    /// Soft delete: đặt IsActive = false.
    /// </summary>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// Lấy voucher theo Id.
    /// </summary>
    Task<VoucherResponse?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy tất cả voucher. Có thể filter theo isActive.
    /// </summary>
    Task<List<VoucherResponse>> GetAllAsync(bool? isActive = null);

    // ====== Grant (Admin) ======

    /// <summary>
    /// Phát voucher cho TẤT CẢ user có Role = Member hiện tại trong hệ thống.
    /// Trả về số lượng user đã nhận.
    /// </summary>
    Task<GrantAllVouchersResult> GrantToAllMembersAsync(string voucherId, string adminId);

    /// <summary>
    /// Phát voucher cho 1 user cụ thể.
    /// </summary>
    Task<bool> GrantToUserAsync(string voucherId, string userId, string adminId);

    // ====== Stats (Admin) ======

    /// <summary>
    /// Thống kê sử dụng voucher.
    /// </summary>
    Task<VoucherStatsResponse?> GetStatsAsync(string voucherId);

    // ====== Apply (User) ======

    /// <summary>
    /// Validate + tính discount cho voucher khi user áp vào đơn.
    /// Không lưu DB - chỉ trả preview.
    /// </summary>
    Task<ApiResponse> ApplyVoucherAsync(string userId, ApplyVoucherRequest request);

    /// <summary>
    /// GIỮ CHỖ (reserve) voucher cho một đơn hàng một cách atomic.
    /// </summary>
    /// <remarks>
    /// Được gọi NGAY khi tạo đơn (trước khi thanh toán) để 1 voucher chỉ có thể
    /// thuộc về 1 đơn tại một thời điểm. Nếu không giữ chỗ, cùng 1 voucher có thể
    /// được áp cho nhiều đơn cùng lúc và mỗi đơn đều được giảm giá.
    /// Lưu ý: giữ chỗ KHÔNG phải là dùng thật — UsedCount chỉ tăng khi thanh toán thành công.
    /// Trả về false nếu voucher đã được giữ chỗ/dùng cho đơn khác, user đã vượt
    /// UsageLimitPerUser, hoặc voucher đã hết lượt sử dụng trên toàn hệ thống.
    /// </remarks>
    Task<bool> ReserveForOrderAsync(string userId, string voucherId, string orderId);

    /// <summary>
    /// GIẢI PHÓNG voucher đang giữ chỗ cho đơn hàng một cách atomic.
    /// </summary>
    /// <remarks>
    /// Gọi khi đơn không còn dùng voucher nữa: khách hủy đơn chưa thanh toán,
    /// thanh toán thất bại, phiên thanh toán hết hạn, hoặc không tạo được phiên thanh toán.
    /// Trả về false nếu voucher không thuộc đơn này (không giải phóng nhầm).
    /// </remarks>
    Task<bool> ReleaseForOrderAsync(string userId, string voucherId, string orderId);

    /// <summary>
    /// GHI NHẬN DÙNG THẬT voucher sau khi thanh toán thành công (atomic, idempotent).
    /// </summary>
    /// <remarks>
    /// Chuyển lượt voucher từ Reserved → Used, tăng UsedCount của voucher và
    /// UsageCount của user. Gọi lại nhiều lần (webhook lặp) chỉ ghi nhận 1 lần.
    /// Trả về false nếu voucher không còn ở trạng thái giữ chỗ cho đúng đơn này.
    /// </remarks>
    Task<bool> ConsumeForOrderAsync(string userId, string voucherId, string orderId);

    // ====== Background ======

    /// <summary>
    /// Disable các voucher đã hết hạn. Gọi từ background job.
    /// Trả về số voucher đã disable.
    /// </summary>
    Task<int> DisableExpiredVouchersAsync();
}

/// <summary>
/// Kết quả phát voucher cho toàn bộ Member.
/// </summary>
public class GrantAllVouchersResult
{
    public int GrantedCount { get; set; }
    public int SkippedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}
