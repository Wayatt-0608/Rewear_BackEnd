using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

// ============================================
// SHIPPING DTOs (Task 9)
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

    /// <summary>Trạng thái vận chuyển (Pending / InTransit / Delivered / Failed).</summary>
    public string Status { get; set; } = string.Empty;

    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao đến tay khách.</summary>
    public DateTime? EstimatedDeliveryDate { get; set; }

    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
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