namespace REWEAR.Domain.Enums;

/// <summary>
/// Trạng thái vận chuyển của đơn hàng (Task 9).
/// Vận chuyển chỉ được tạo sau khi đơn đã thu tiền, nên không tồn tại
/// trạng thái "chờ thanh toán" ở đây.
/// </summary>
public enum ShippingStatus
{
    /// <summary>Đã chuẩn bị giao, chưa bàn giao cho đơn vị vận chuyển.</summary>
    Pending = 0,

    /// <summary>Đã bàn giao cho đơn vị vận chuyển và đang trên đường tới khách.</summary>
    InTransit = 1,

    /// <summary>Đã giao thành công cho khách.</summary>
    Delivered = 2,

    /// <summary>Giao thất bại / đơn vị vận chuyển trả lại.</summary>
    Failed = 3
}