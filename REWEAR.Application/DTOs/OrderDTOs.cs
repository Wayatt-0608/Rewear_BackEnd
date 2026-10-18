using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

// ============================================
// ORDER DTOs (Task 6)
// ============================================

/// <summary>
/// Một dòng sản phẩm trong đơn hàng, đã join sẵn thông tin hiển thị.
/// </summary>
public class OrderItemResponse
{
    public string Id { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    /// <summary>Giá đơn vị đã chốt tại thời điểm đặt hàng.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Thành tiền của dòng = UnitPrice * Quantity.</summary>
    public decimal LineTotal { get; set; }

    // Thông tin sản phẩm đã snapshot
    public string ProductTitle { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// Sản phẩm trong đơn còn tồn tại trong catalog hay không
    /// (nếu bị soft-delete, vẫn hiển thị nhờ snapshot).
    /// </summary>
    public bool ProductExists { get; set; } = true;
}

/// <summary>
/// Thông tin địa chỉ giao hàng đã snapshot trong đơn.
/// </summary>
public class OrderShippingAddressResponse
{
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string StreetAddress { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>Địa chỉ đầy đủ đã ghép sẵn để hiển thị.</summary>
    public string FullAddress { get; set; } = string.Empty;
}

/// <summary>
/// Response đầy đủ một đơn hàng.
/// </summary>
public class OrderResponse
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Mã đơn hiển thị cho người dùng (vd: RW-20261005-A1B2C3).</summary>
    public string OrderCode { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public List<OrderItemResponse> Items { get; set; } = new();

    public OrderShippingAddressResponse ShippingAddress { get; set; } = new();

    /// <summary>Phương thức thanh toán đã chọn (PayOs).</summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>Tổng số lượng sản phẩm trong đơn.</summary>
    public int TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>Trạng thái đơn (AwaitingPayment / Confirmed / Shipping / Delivered / Cancelled / PaymentExpired).</summary>
    public string Status { get; set; } = string.Empty;

    public string? CancelReason { get; set; }

    /// <summary>Mã vận đơn (null nếu chưa gửi hàng).</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao đến tay khách (null nếu chưa chốt).</summary>
    public DateTime? EstimatedDeliveryDate { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Đơn đã kết thúc chưa (Delivered / Cancelled / PaymentExpired) — FE dùng để disable nút thao tác.</summary>
    public bool IsFinalized { get; set; }

    /// <summary>Đơn còn trong thời gian cho phép hủy không (chỉ AwaitingPayment).</summary>
    public bool CanCancel { get; set; }
}

/// <summary>
/// Query parameters cho API lấy danh sách đơn hàng.
/// </summary>
public class OrderQueryParameters
{
    /// <summary>Trạng thái đơn (null = tất cả).</summary>
    public OrderStatus? Status { get; set; }

    /// <summary>Số bản ghi mỗi trang.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Trang hiện tại (bắt đầu từ 1).</summary>
    public int Page { get; set; } = 1;
}

/// <summary>
/// Response phân trang cho danh sách đơn hàng.
/// </summary>
public class PagedOrderResponse
{
    public List<OrderResponse> Orders { get; set; } = new();

    public int TotalCount { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

// ============================================
// ORDER TRACKING DTOs (Task 7)
// ============================================

/// <summary>
/// Một mốc trong timeline trạng thái đơn hàng.
/// </summary>
public class OrderStatusHistoryResponse
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Trạng thái trước khi đổi (null = đơn vừa tạo).</summary>
    public string? FromStatus { get; set; }

    /// <summary>Trạng thái sau khi đổi.</summary>
    public string ToStatus { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>Ai thực hiện thay đổi (Customer / Staff / System / Shipper).</summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>Id người thao tác (null nếu là System).</summary>
    public string? ChangedByUserId { get; set; }

    /// <summary>Tên người thao tác tại thời điểm đổi (snapshot, FE dùng để hiển thị).</summary>
    public string? ChangedByName { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response theo dõi đơn: thông tin vận chuyển + timeline trạng thái + trạng thái thanh toán.
/// </summary>
public class OrderTrackingResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Trạng thái hiện tại của đơn.</summary>
    public string CurrentStatus { get; set; } = string.Empty;

    /// <summary>Trạng thái thanh toán hiện tại (Paid / Pending / Expired / Refunded).</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>Mã vận đơn (null nếu chưa gửi hàng).</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao (null nếu chưa chốt).</summary>
    public DateTime? EstimatedDeliveryDate { get; set; }

    /// <summary>Toàn bộ timeline, cũ nhất trước.</summary>
    public List<OrderStatusHistoryResponse> Timeline { get; set; } = new();
}

/// <summary>
/// Request cập nhật trạng thái đơn + thông tin vận chuyển (Admin/Shipper).
/// </summary>
public class UpdateOrderStatusRequest
{
    /// <summary>Trạng thái mới của đơn.</summary>
    public OrderStatus Status { get; set; }

    /// <summary>Ghi chú cho mốc timeline (vd: "Đã bàn giao cho shipper").</summary>
    public string? Note { get; set; }

    /// <summary>Mã vận đơn (bắt buộc khi chuyển sang trạng thái Shipping).</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao đến tay khách.</summary>
    public DateTime? EstimatedDeliveryDate { get; set; }
}