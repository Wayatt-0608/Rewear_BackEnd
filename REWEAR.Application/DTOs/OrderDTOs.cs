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

    /// <summary>Phương thức thanh toán đã chọn (COD / Banking / Momo / VNPay).</summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>Tổng số lượng sản phẩm trong đơn.</summary>
    public int TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>Trạng thái đơn (Pending / Confirmed / Shipping / Delivered / Cancelled).</summary>
    public string Status { get; set; } = string.Empty;

    public string? CancelReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Đơn đã kết thúc chưa (Delivered / Cancelled) — FE dùng để disable nút thao tác.</summary>
    public bool IsFinalized { get; set; }

    /// <summary>Đơn còn trong thời gian cho phép hủy không (chỉ Pending/Confirmed).</summary>
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