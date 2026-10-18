using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

// ============================================
// PAYMENT DTOs (Task 8)
// ============================================

/// <summary>
/// Kết quả tạo phiên thanh toán sau khi checkout. Frontend dùng thông tin này
/// để chuyển khách tới trang thanh toán của PayOS hoặc hiển thị QR ngay tại chỗ.
/// </summary>
public class CheckoutSessionResponse
{
    /// <summary>Id đơn hàng vừa tạo.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Mã đơn hiển thị cho người dùng (vd: RW-20261005-A1B2C3).</summary>
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Tổng tiền khách phải trả (REWEAR miễn phí vận chuyển nên bằng tổng tiền hàng).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Đường dẫn trang thanh toán của PayOS để chuyển hướng khách.</summary>
    public string? CheckoutUrl { get; set; }

    /// <summary>Ảnh QR thanh toán (base64 hoặc URL tùy PayOS trả về).</summary>
    public string? QrCode { get; set; }

    /// <summary>paymentLinkId của PayOS, dùng để tra cứu giao dịch khi webhook bị mất.</summary>
    public string? PaymentLinkId { get; set; }

    /// <summary>
    /// Thời điểm hết hạn của phiên thanh toán (5 phút kể từ lúc tạo đơn - DEV/TEST).
    /// Frontend nên hiển thị đồng hồ đếm ngược và cảnh báo khách trước khi hết giờ.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Số giây còn lại để hoàn tất thanh toán (tiện cho FE đếm ngược).</summary>
    public int ExpiresInSeconds { get; set; }
}

/// <summary>
/// Thông tin 1 lần thanh toán (hiển thị trên trang chi tiết đơn).
/// </summary>
public class PaymentResponse
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Phương thức thanh toán (PayOs).</summary>
    public string Method { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>Trạng thái thanh toán (Pending / Paid / Failed / Expired / Refunded).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Đường dẫn trang thanh toán (chỉ có khi phiên còn Pending).</summary>
    public string? CheckoutUrl { get; set; }

    public string? QrCode { get; set; }

    /// <summary>Số tiền PayOS thực sự ghi nhận.</summary>
    public decimal? ReceivedAmount { get; set; }

    /// <summary>Thời điểm thanh toán thành công (null nếu chưa trả).</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Thời điểm hết hạn phiên thanh toán.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Trạng thái thanh toán của đơn (dùng cho endpoint tra cứu mà FE gọi sau khi
/// quay lại từ trang PayOS - đường xác nhận thứ 2 bên cạnh webhook).
/// </summary>
public class PaymentStatusResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Trạng thái thanh toán hiện tại.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>Trạng thái đơn hàng hiện tại (đã cập nhật theo kết quả thanh toán).</summary>
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>Đơn đã thu tiền thành công chưa.</summary>
    public bool IsPaid { get; set; }

    /// <summary>Phiên thanh toán đã hết hạn chưa (đơn sẽ bị tự đóng).</summary>
    public bool IsExpired { get; set; }

    /// <summary>Số giây còn lại để thanh toán (0 nếu đã hết hạn hoặc đã trả).</summary>
    public int ExpiresInSeconds { get; set; }
}

/// <summary>
/// Response lịch sử thanh toán của đơn.
/// </summary>
public class PaymentHistoryResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Phiên thanh toán mới nhất (mức thanh toán hiện tại của đơn).</summary>
    public PaymentResponse? CurrentPayment { get; set; }

    /// <summary>Toàn bộ lịch sử các phiên thanh toán, mới nhất trước.</summary>
    public List<PaymentResponse> Payments { get; set; } = new();

    /// <summary>Đơn đã thu tiền thành công chưa.</summary>
    public bool IsPaid { get; set; }
}