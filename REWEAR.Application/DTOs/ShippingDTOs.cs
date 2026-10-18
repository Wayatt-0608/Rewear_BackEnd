using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

// ============================================
// SHIPPING DTOs (Task 9 + Task Shipper)
// ============================================

/// <summary>
/// Thông tin vận chuyển (hiển thị trên trang chi tiết đơn).
/// </summary>
public class ShippingResponse
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Phương thức vận chuyển (Standard / Express).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Phí vận chuyển (REWEAR miễn phí nên luôn = 0).</summary>
    public decimal Fee { get; set; }

    /// <summary>
    /// Trạng thái vận chuyển (Pending / InTransit / Delivered / Failed / Returned).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao đến tay khách.</summary>
    public DateTime? EstimatedDeliveryDate { get; set; }

    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    // ====== PHÂN BỔ SHIPPER (Task Shipper) ======

    /// <summary>Id shipper được phân bổ (null nếu chưa giao cho shipper cụ thể).</summary>
    public string? ShipperId { get; set; }

    /// <summary>Tên shipper tại thời điểm phân bổ (snapshot).</summary>
    public string? ShipperName { get; set; }

    /// <summary>Số lần giao thất bại của shipper cho đơn này.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Lý do thất bại gần nhất.</summary>
    public string? LastFailureReason { get; set; }

    /// <summary>Thời điểm thất bại gần nhất.</summary>
    public DateTime? LastFailedAt { get; set; }
}

/// <summary>
/// Bảng giá vận chuyển để frontend hiển thị lựa chọn khi checkout.
/// </summary>
public class ShippingRateResponse
{
    /// <summary>Phương thức vận chuyển.</summary>
    public ShippingMethod Method { get; set; }

    /// <summary>Tên hiển thị (vd: "Giao hàng nhanh").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Phí vận chuyển (đồng). REWEAR đang miễn phí toàn bộ nên = 0.</summary>
    public decimal Fee { get; set; }

    /// <summary>Thời gian giao hàng ước tính (vd: "1-2 ngày").</summary>
    public string EstimatedDays { get; set; } = string.Empty;
}

/// <summary>
/// Request tạo vận chuyển cho một đơn đã thanh toán (Admin/Staff).
/// </summary>
public class CreateShippingRequest
{
    /// <summary>Id shipper được phân bổ. Bắt buộc đối với Admin/Staff.</summary>
    public string? ShipperId { get; set; }

    /// <summary>Phương thức vận chuyển. Mặc định Standard nếu không truyền.</summary>
    public ShippingMethod Method { get; set; } = ShippingMethod.Standard;

    /// <summary>Đơn vị vận chuyển (vd: "GHN", "GHTK").</summary>
    public string? Carrier { get; set; }
}

/// <summary>
/// Request cập nhật thông tin vận chuyển (nhân viên Admin/Shipper).
/// </summary>
public class UpdateShippingRequest
{
    /// <summary>Đơn vị vận chuyển (vd: "GHN", "GHTK").</summary>
    public string? Carrier { get; set; }

    /// <summary>Mã vận đơn. Bắt buộc khi bàn giao cho đơn vị vận chuyển.</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao đến tay khách.</summary>
    public DateTime? EstimatedDeliveryDate { get; set; }

    /// <summary>Ghi chú vận chuyển (vd: lý do giao thất bại).</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Request shipper cập nhật trạng thái vận chuyển (nhận đơn, đang giao, giao xong/thất bại).
/// </summary>
public class UpdateShippingStatusRequest
{
    /// <summary>
    /// Mã trạng thái vận chuyển mới theo <see cref="ShippingStatus"/>:
    /// 0 = Pending, 1 = InTransit, 2 = Delivered, 3 = Failed, 4 = Returned.
    /// </summary>
    public int Status { get; set; }

    /// <summary>Ghi chú cho mốc timeline (vd: lý do thất bại).</summary>
    public string? Note { get; set; }

    /// <summary>Mã vận đơn (nhập khi chuyển sang InTransit).</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Số lần thử giao (chỉ dùng khi status = Failed). Server sẽ tự tăng AttemptCount
    /// lên 1 nếu không truyền hoặc dùng giá trị này làm tổng nếu muốn đồng bộ từ client.
    /// </summary>
    public int? AttemptCount { get; set; }
}

/// <summary>
/// Request admin yêu cầu giao lại (sau khi shipper trả hàng về kho do thất bại nhiều lần).
/// </summary>
public class ReshipRequest
{
    /// <summary>Id shipper mới cho lần giao lại. Null = giữ nguyên shipper cũ.</summary>
    public string? ShipperId { get; set; }

    /// <summary>Ghi chú (vd: "Đổi shipper do khu vực xa").</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Request admin hủy vĩnh viễn đơn đã Failed (buyer mất tiền + hàng, sản phẩm vẫn Sold).
/// </summary>
public class CancelPermanentRequest
{
    /// <summary>Lý do hủy (ghi vào timeline).</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Một dòng trong bảng đơn chờ giao (cho Admin/Staff xem).
/// </summary>
public class ShippingQueueItem
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool HasShipping { get; set; }
    public string? CurrentShipperId { get; set; }
    public string? CurrentShipperName { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Response danh sách đơn chờ giao (Admin/Staff).
/// </summary>
public class ShippingQueueResponse
{
    public List<ShippingQueueItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>
/// Một dòng trong danh sách đơn của shipper (dashboard).
/// </summary>
public class MyOrderItem
{
    public string ShippingId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerPhone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string ShippingStatus { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public int AttemptCount { get; set; }
    public string? LastFailureReason { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
}

/// <summary>
/// Response danh sách đơn của shipper (filter theo status).
/// </summary>
public class MyOrdersResponse
{
    public List<MyOrderItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public string? ShipperId { get; set; }
    public string? ShipperName { get; set; }
    public ShippingStatus? FilterStatus { get; set; }
}

/// <summary>
/// Response danh sách toàn bộ shipping (AdminOnly, cho dashboard).
/// </summary>
public class ShippingListResponse
{
    public List<ShippingResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public ShippingStatus? FilterStatus { get; set; }
}
