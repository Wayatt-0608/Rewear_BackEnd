namespace REWEAR.Domain.Enums;

/// <summary>
/// Vòng đời của một đơn hàng trên REWEAR.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Đơn vừa được tạo từ giỏ hàng, chờ người bán/admin xác nhận.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Đã xác nhận, sản phẩm được chốt (Reserved) và chuẩn bị giao.
    /// </summary>
    Confirmed = 1,

    /// <summary>
    /// Đang trong quá trình vận chuyển.
    /// </summary>
    Shipping = 2,

    /// <summary>
    /// Đã giao thành công. Đây là trạng thái cuối cho phép đánh giá (Task 11).
    /// </summary>
    Delivered = 3,

    /// <summary>
    /// Đơn bị hủy. Sản phẩm phải được trả về Available (Task 12 refund).
    /// </summary>
    Cancelled = 4
}