namespace REWEAR.Domain.Enums;

/// <summary>
/// Vòng đời của 1 lượt voucher trong "kho voucher" của user.
/// </summary>
/// <remarks>
/// Ba trạng thái này là ba thời điểm khác nhau, KHÔNG được gộp làm một:
/// - <see cref="Available"/>: voucher đã được cấp cho user, chưa gắn với đơn nào.
/// - <see cref="Reserved"/>: voucher đang được GIỮ CHỖ cho một đơn chờ thanh toán.
///   Chưa bị tiêu thụ thật — nếu đơn bị hủy/hết hạn/thanh toán lỗi thì voucher
///   phải được trả lại kho (chuyển về Available).
/// - <see cref="Used"/>: voucher đã được SỬ DỤNG THẬT (đơn đã thanh toán thành công).
///   Đây mới là lúc voucher thực sự bị tiêu thụ.
/// </remarks>
public enum UserVoucherStatus
{
    /// <summary>Đã cấp, chưa dùng và chưa giữ chỗ cho đơn nào.</summary>
    Available = 0,

    /// <summary>Đang giữ chỗ cho một đơn hàng chờ thanh toán (chưa tiêu thụ thật).</summary>
    Reserved = 1,

    /// <summary>Đã dùng thật cho một đơn hàng đã thanh toán thành công.</summary>
    Used = 2
}
