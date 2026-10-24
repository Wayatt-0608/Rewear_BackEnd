using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service quản lý "Kho voucher" của user (Task 10).
/// </summary>
public interface IUserVoucherService
{
    /// <summary>
    /// Lấy toàn bộ voucher của user hiện tại (FE dùng cho trang "Kho voucher của tôi").
    /// </summary>
    /// <param name="filter">
    /// "all" = tất cả; "available" = còn dùng được; "used" = đã dùng; "expired" = hết hạn/không dùng được.
    /// </param>
    Task<List<UserVoucherResponse>> GetMyCollectionAsync(string userId, string filter = "all");

    /// <summary>
    /// Lấy chi tiết 1 UserVoucher (kèm thông tin voucher).
    /// </summary>
    Task<UserVoucherResponse?> GetByIdAsync(string id, string userId);
}
