namespace REWEAR.Domain.Enums;

/// <summary>
/// Trạng thái thanh toán của một đơn hàng (Task 8).
/// REWEAR áp dụng mô hình "trả tiền trước": đơn chỉ được chuyển sang giao hàng
/// khi thanh toán đã ở trạng thái Paid.
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Đã tạo phiên thanh toán, khách chưa trả tiền. Sản phẩm đang được giữ chỗ
    /// trong thời gian chờ (<see cref="Payment.ExpiresAt"/>); hết giờ thì chuyển sang Expired.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Khách đã thanh toán thành công (xác nhận qua webhook hoặc đối soát chủ động).
    /// Đơn có thể chuyển sang Confirmed để giao hàng.
    /// </summary>
    Paid = 1,

    /// <summary>
    /// Giao dịch thất bại: khách hủy ở trang PayOS, hoặc ngân hàng từ chối giao dịch.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Hết thời gian chờ thanh toán, hệ thống tự đóng phiên.
    /// Sản phẩm được tự động trả về kho và đơn chuyển sang PaymentExpired.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Đã hoàn tiền. Xảy ra khi khách hủy đơn đã thanh toán, hoặc đơn bị huỷ sau khi thu tiền.
    /// </summary>
    Refunded = 4
}