namespace REWEAR.Domain.Enums;

/// <summary>
/// Ai là người thực hiện thay đổi trạng thái đơn (hiển thị trên timeline - Task 7).
/// </summary>
public enum OrderStatusChangedBy
{
    /// <summary>Người mua tự thao tác (hủy đơn).</summary>
    Customer = 0,

    /// <summary>Nhân viên REWEAR thao tác (xác nhận, gửi hàng, giao xong).</summary>
    Staff = 1,

    /// <summary>Hệ thống tự động (webhook thanh toán, quét hạn phiên thanh toán).</summary>
    System = 2,

    /// <summary>Shipper cập nhật trạng thái giao hàng (nhận đơn, đang giao, giao xong/thất bại).</summary>
    Shipper = 3
}
