namespace REWEAR.Domain.Enums;

/// <summary>
/// Vòng đời của một đơn hàng trên REWEAR.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Đã tạo đơn và đang giữ chỗ sản phẩm, chờ khách thanh toán qua PayOS.
    /// Có thời hạn 15 phút; quá hạn thì chuyển sang <see cref="PaymentExpired"/>.
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
    /// Đã giao thành công. Đây là trạng thái cuối cho phép đánh giá (Task 11),
    /// đồng thời là thời điểm sản phẩm được chốt Sold.
    /// </summary>
    Delivered = 3,

    /// <summary>
    /// Người mua chủ động hủy đơn. Sản phẩm được trả về Available;
    /// nếu đã thanh toán thì tiền được hoàn lại qua PayOS.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Hết thời gian thanh toán, hệ thống tự đóng đơn.
    /// Tách riêng với Cancelled để đo được tỷ lệ bỏ giỏ do khách không hoàn tất thanh toán.
    /// </summary>
    PaymentExpired = 5
}