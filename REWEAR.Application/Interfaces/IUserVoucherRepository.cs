using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho UserVoucher - liên kết User ↔ Voucher (Task 10).
/// </summary>
/// <remarks>
/// Mỗi (user, voucher) có đúng 1 bản ghi = 1 "lượt voucher" của user đó.
/// Bản ghi đi qua 3 trạng thái: Available → Reserved → Used.
/// Mọi chuyển trạng thái đều là atomic update có điều kiện (compare-and-set)
/// để tránh 2 luồng cùng lúc chiếm/hoàn/ghi nhận 1 lượt voucher.
/// </remarks>
public interface IUserVoucherRepository
{
    /// <summary>
    /// Lấy UserVoucher theo Id.
    /// </summary>
    Task<UserVoucher?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy tất cả UserVoucher của 1 user. Bao gồm cả đã dùng, hết hạn, v.v.
    /// </summary>
    Task<List<UserVoucher>> GetByUserIdAsync(string userId);

    /// <summary>
    /// Lấy UserVoucher theo user + voucher (1 user chỉ có 1 record / voucher).
    /// </summary>
    Task<UserVoucher?> GetByUserAndVoucherAsync(string userId, string voucherId);

    /// <summary>
    /// Tạo UserVoucher mới.
    /// </summary>
    Task CreateAsync(UserVoucher userVoucher);

    /// <summary>
    /// Cập nhật UserVoucher.
    /// </summary>
    Task UpdateAsync(UserVoucher userVoucher);

    /// <summary>
    /// GIỮ CHỖ voucher cho một đơn: Available → Reserved (atomic).
    /// </summary>
    /// <remarks>
    /// Chỉ thành công khi lượt voucher của user đó đang Available (chưa giữ chỗ,
    /// chưa dùng) VÀ user chưa vượt <paramref name="usageLimitPerUser"/>.
    /// Trả về false nếu không thỏa → voucher không bị chiếm, không bị thay đổi.
    /// </remarks>
    /// <param name="orderId">Id đơn hàng giữ chỗ voucher.</param>
    /// <param name="usageLimitPerUser">Số lần user được phép dùng voucher này.</param>
    Task<bool> TryReserveAsync(
        string userId, string voucherId, string orderId, int usageLimitPerUser);

    /// <summary>
    /// GIẢI PHÓNG voucher đang giữ chỗ: Reserved → Available (atomic).
    /// </summary>
    /// <remarks>
    /// Chỉ thành công khi lượt voucher đang Reserved VÀ đang gắn với đúng
    /// <paramref name="orderId"/>. Trả về false nếu không khớp → không bao giờ
    /// giải phóng nhầm voucher của đơn khác.
    /// </remarks>
    Task<bool> TryReleaseAsync(string userId, string voucherId, string orderId);

    /// <summary>
    /// GHI NHẬN DÙNG THẬT: Reserved → Used (atomic), tăng UsageCount của user.
    /// </summary>
    /// <remarks>
    /// Chỉ thành công khi lượt voucher đang Reserved VÀ gắn với đúng
    /// <paramref name="orderId"/>. Khi user đã dùng đủ <paramref name="usageLimitPerUser"/>
    /// lượt thì lượt voucher chuyển sang Used (hạn dùng); nếu còn lượt thì quay lại
    /// Available để user dùng tiếp. Gọi lại nhiều lần chỉ ghi nhận 1 lần.
    /// </remarks>
    Task<bool> TryConsumeAsync(
        string userId, string voucherId, string orderId, int usageLimitPerUser);

    /// <summary>
    /// Giảm UsageCount của user xuống 1 (atomic, tối thiểu 0).
    /// </summary>
    /// <remarks>
    /// Dùng để hoàn tát khi ghi nhận dùng thật thất bại ở phía voucher, để
    /// UsageCount của user không bị tính nhầm lượt đã dùng.
    /// </remarks>
    Task<bool> DecrementUsageCountAsync(string userId, string voucherId);

    /// <summary>
    /// Đếm tổng số UserVoucher của 1 voucher (kể cả đã dùng và chưa dùng).
    /// Dùng cho stats.
    /// </summary>
    Task<int> CountByVoucherAsync(string voucherId);

    /// <summary>
    /// Đếm tổng số UserVoucher đã dùng thật của 1 voucher.
    /// </summary>
    Task<int> CountUsedByVoucherAsync(string voucherId);

    /// <summary>
    /// Đếm số UserVoucher đang GIỮ CHỖ (chưa thanh toán).
    /// </summary>
    /// <remarks>
    /// Lượt giữ chỗ vẫn chưa bị tiêu thụ nhưng đã chiếm 1 lượt của voucher,
    /// nên phải trừ khỏi số lượt còn khả dụng trong thống kê.
    /// </remarks>
    Task<int> CountReservedByVoucherAsync(string voucherId);

    /// <summary>
    /// Lấy tất cả UserVoucher của 1 voucher (cho admin stats).
    /// </summary>
    Task<List<UserVoucher>> GetByVoucherIdAsync(string voucherId);

    /// <summary>
    /// Insert nhiều UserVoucher cùng lúc (dùng cho grant-all).
    /// </summary>
    Task CreateManyAsync(IEnumerable<UserVoucher> userVouchers);
}
