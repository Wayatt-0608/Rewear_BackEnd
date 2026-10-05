using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Một lần thanh toán của đơn hàng (Task 8).
/// Lưu riêng collection (không nhúng vào Order) vì một đơn có thể có nhiều
/// phiên thanh toán (hết hạn rồi thử lại) và cần tra cứu lịch sử tài chính.
/// </summary>
public class Payment
{
    /// <summary>Khóa chính.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Id đơn hàng liên quan.</summary>
    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Mã đơn hiển thị, snapshot để tra cứu nhanh không cần join Orders.</summary>
    [BsonElement("orderCode")]
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Id người mua.</summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Phương thức thanh toán (hiện chỉ có PayOs).</summary>
    [BsonElement("method")]
    [BsonRepresentation(BsonType.String)]
    public PaymentMethod Method { get; set; } = PaymentMethod.PayOs;

    /// <summary>Số tiền cần thanh toán (= TotalAmount của đơn).</summary>
    [BsonElement("amount")]
    public decimal Amount { get; set; }

    /// <summary>Trạng thái thanh toán hiện tại của phiên này.</summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    // ====== THÔNG TIN TỪ PAYOS ======

    /// <summary>
    /// paymentLinkId do PayOS cấp khi tạo payment request. Dùng để tra cứu lại
    /// giao dịch khi webhook bị mất.
    /// </summary>
    [BsonElement("paymentLinkId")]
    public string? PaymentLinkId { get; set; }

    /// <summary>
    /// Id giao dịch thật từ PayOS. Duy nhất trong hệ thống: nếu 2 giao dịch khác nhau
    /// cùng nhận 1 transactionId thì đó là dấu hiệu dữ liệu bị lỗi hoặc giả mạo.
    /// </summary>
    [BsonElement("payosTransactionId")]
    public string? PayOsTransactionId { get; set; }

    /// <summary>Đường dẫn trang thanh toán của PayOS để chuyển hướng khách hàng.</summary>
    [BsonElement("checkoutUrl")]
    public string? CheckoutUrl { get; set; }

    /// <summary>Ảnh QR thanh toán để frontend hiển thị tại trang checkout.</summary>
    [BsonElement("qrCodeUrl")]
    public string? QrCodeUrl { get; set; }

    /// <summary>
    /// Thời điểm hết hạn của phiên thanh toán. Quá mốc này, background job sẽ đóng phiên
    /// và trả sản phẩm về kho.
    /// </summary>
    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>Số tiền PayOS thực sự ghi nhận (có thể khác Amount nếu giao dịch lệch).</summary>
    [BsonElement("receivedAmount")]
    public decimal? ReceivedAmount { get; set; }

    /// <summary>Thời điểm xác nhận thanh toán thành công.</summary>
    [BsonElement("paidAt")]
    public DateTime? PaidAt { get; set; }

    /// <summary>Thời điểm hoàn tiền thành công (nếu có).</summary>
    [BsonElement("refundedAt")]
    public DateTime? RefundedAt { get; set; }

    /// <summary>Ghi chú (vd: lý do khách hủy ở trang PayOS).</summary>
    [BsonElement("note")]
    public string? Note { get; set; }

    /// <summary>Thời điểm tạo bản ghi.</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm cập nhật gần nhất.</summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Phiên thanh toán đã hết hạn và chưa nhận được tiền chưa.</summary>
    [BsonIgnore]
    public bool IsExpiredButUnpaid =>
        Status == PaymentStatus.Pending && DateTime.UtcNow > ExpiresAt;
}