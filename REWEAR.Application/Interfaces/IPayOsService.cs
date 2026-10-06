namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface làm việc với cổng thanh toán PayOS (Task 8).
/// </summary>
/// <remarks>
/// Hệ thống dùng polling thay cho webhook: frontend gọi GET /api/payments/{id}/status,
/// backend chủ động gọi PayOS qua <see cref="GetTransactionAsync"/> để hỏi trạng thái.
/// Không cần xác minh chữ ký vì không nhận callback từ PayOS nữa.
/// </remarks>
public interface IPayOsService
{
    /// <summary>
    /// Tạo payment request trên PayOS và trả về thông tin trang thanh toán.
    /// </summary>
    /// <param name="orderCode">Mã đơn của REWEAR, dùng làm reference để đối soát.</param>
    /// <param name="amount">Số tiền thanh toán (đồng).</param>
    /// <param name="returnUrl">URL FE gọi lại sau khi khách thanh toán xong.</param>
    /// <param name="cancelUrl">URL FE gọi lại khi khách hủy thanh toán.</param>
    /// <param name="expiredAt">Thời điểm hết hạn phiên thanh toán.</param>
    /// <returns>Thông tin phiên thanh toán, hoặc null nếu PayOS từ chối.</returns>
    Task<PayOsCreateResult?> CreatePaymentRequestAsync(
        string orderCode, decimal amount,
        string returnUrl, string cancelUrl,
        DateTime expiredAt);

    /// <summary>
    /// Tra cứu chi tiết giao dịch từ PayOS. Đây là API backend dùng để polling
    /// trạng thái thay cho webhook: PAID / CANCELLED / EXPIRED / PENDING.
    /// </summary>
    Task<PayOsTransactionInfo?> GetTransactionAsync(string transactionId);

    /// <summary>
    /// Hoàn tiền một giao dịch đã thanh toán (dùng khi khách hủy đơn đã trả tiền).
    /// </summary>
    /// <param name="transactionId">Id giao dịch cần hoàn.</param>
    /// <param name="reason">Lý do hoàn (hiển thị trên lịch sử giao dịch).</param>
    Task<PayOsRefundResult> RefundAsync(string transactionId, string? reason);

    /// <summary>
    /// Đăng ký URL webhook với PayOS. PayOS sẽ test URL bằng cách gọi POST
    /// vào đó; nếu endpoint trả response đúng format, URL sẽ được lưu lại và
    /// PayOS sẽ gọi mỗi khi có giao dịch thay đổi trạng thái.
    /// </summary>
    /// <param name="webhookUrl">URL đầy đủ (https://...) của webhook endpoint.</param>
    /// <returns>True nếu PayOS chấp nhận URL.</returns>
    Task<bool> ConfirmWebhookAsync(string webhookUrl);
}

/// <summary>
/// Kết quả tạo payment request trên PayOS.
/// </summary>
public class PayOsCreateResult
{
    /// <summary>Id giao dịch do PayOS cấp.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>paymentLinkId dùng để tra cứu giao dịch khi webhook bị mất.</summary>
    public string PaymentLinkId { get; set; } = string.Empty;

    /// <summary>URL trang thanh toán để chuyển hướng khách hàng.</summary>
    public string CheckoutUrl { get; set; } = string.Empty;

    /// <summary>Ảnh QR thanh toán (dạng chuỗi base64 hoặc URL, tuỳ PayOS trả về).</summary>
    public string? QrCode { get; set; }
}

/// <summary>
/// Chi tiết giao dịch lấy từ PayOS (dùng cho polling trạng thái).
/// </summary>
public class PayOsTransactionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string PaymentLinkId { get; set; } = string.Empty;
    public int Amount { get; set; }
    public decimal PaidAmount { get; set; }

    /// <summary>Trạng thái giao dịch (PAID / PENDING / FAILED / EXPIRED / CANCELLED).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Kết quả hoàn tiền từ PayOS.
/// </summary>
public class PayOsRefundResult
{
    public bool Success { get; set; }
    public string? RefundId { get; set; }
    public string? Message { get; set; }
}
