using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

// ============================================
// CHECKOUT DTOs (Task 5)
// ============================================

/// <summary>
/// Request đặt hàng (checkout) từ giỏ hàng hiện tại.
/// </summary>
public class CheckoutRequest
{
    /// <summary>
    /// Id địa chỉ giao hàng đã lưu trong hồ sơ (Task 2: POST /api/profile/addresses).
    /// </summary>
    public string ShippingAddressId { get; set; } = string.Empty;

    /// <summary>
    /// Phương thức thanh toán. COD = thanh toán khi nhận hàng.
    /// Các phương thức online sẽ được xử lý ở Task 8 (Payment).
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.COD;

    /// <summary>
    /// Ghi chú cho đơn hàng (vd: "Gọi trước khi giao").
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// Request hủy đơn hàng.
/// </summary>
public class CancelOrderRequest
{
    /// <summary>
    /// Lý do hủy (tùy chọn). Nếu bỏ trống sẽ mặc định là "Người mua hủy đơn."
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Kết quả kiểm tra giỏ hàng trước khi checkout.
/// Trả về cả danh sách lỗi chi tiết để FE hiển thị từng sản phẩm bị ảnh hưởng.
/// </summary>
public class CartValidationResult
{
    /// <summary>Giỏ hàng có hợp lệ để đặt hàng không.</summary>
    public bool IsValid { get; set; }

    /// <summary>Danh sách lỗi (rỗng nếu IsValid = true).</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Cảnh báo không chặn đặt hàng (vd: giá đã thay đổi).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Tổng tiền sau khi dùng giá hiện tại (không phải giá snapshot trong giỏ).
    /// </summary>
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// Response của một lỗi khi checkout thất bại.
/// </summary>
public class CheckoutErrorResponse
{
    /// <summary>Thông báo lỗi tổng quát cho người dùng.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Danh sách lỗi chi tiết.</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Danh sách cảnh báo (vd: giá thay đổi, sản phẩm không còn khả dụng).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Id của sản phẩm bị ảnh hưởng (dùng để frontend highlight trong giỏ hàng).
    /// </summary>
    public string? ProductId { get; set; }

    /// <summary>Mã lỗi nghiệp vụ (vd: CART_EMPTY, PRODUCT_SOLD, ADDRESS_INVALID).</summary>
    public string? ErrorCode { get; set; } = string.Empty;
}