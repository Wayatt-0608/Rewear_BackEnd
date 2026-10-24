namespace REWEAR.Domain.Enums;

/// <summary>
/// Vòng đời của một đơn hàng trên REWEAR.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Đã tạo đơn và đang giữ chỗ sản phẩm, chờ khách thanh toán qua PayOS.
    /// Deadline được lưu trên order, mặc định 15 phút; quá hạn chuyển sang <see cref="PaymentExpired"/>.
    /// </summary>
    AwaitingPayment = 0,

    /// <summary>
    /// Đã thu tiền thành công, đơn được chốt và chuẩn bị giao.
    /// Không có đường nào đi tới Confirmed mà chưa có PaymentStatus.Paid.
    /// </summary>
    Confirmed = 1,

    /// <summary>
    /// Đang trong quá trình vận chuyển. Bắt buộc đã có mã vận đơn.
    /// </summary>
    Shipping = 2,

    /// <summary>
    /// Đã giao thành công. Tồn đã được chốt ở bước thanh toán, không trừ lần nữa.
    /// </summary>
    Delivered = 3,

    /// <summary>
    /// Hủy đơn chưa thu tiền, hoặc đã có xác nhận hoàn tiền thực tế trước giao hàng.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Hết thời gian thanh toán, hệ thống tự đóng đơn.
    /// Tách riêng với Cancelled để đo được tỷ lệ bỏ giỏ do khách không hoàn tất thanh toán.
    /// </summary>
    PaymentExpired = 5,

    /// <summary>
    /// Giao hàng thất bại sau khi đã rời kho (shipper không giao được, khách từ chối nhận...).
    /// Admin quyết định giao lại hoặc tiếp nhận/kiểm định hàng hoàn trước khi hoàn tiền.
    /// </summary>
    Failed = 6,
    ReturnRequested = 7,
    ReturnReceived = 8,
    Returned = 9
}
