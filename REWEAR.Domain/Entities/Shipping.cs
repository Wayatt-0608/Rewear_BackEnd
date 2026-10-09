using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Thông tin vận chuyển của một đơn hàng (Task 9).
/// Tách khỏi Order vì vận chuyển có vòng đời riêng (chuẩn bị giao, đang chuyển,
/// giao xong) và cần tra cứu độc lập cho dashboard shipper.
/// </summary>
[BsonIgnoreExtraElements]
public class Shipping
{
    /// <summary>Khóa chính.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Id đơn hàng. Mỗi đơn chỉ có 1 bản ghi vận chuyển.</summary>
    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Mã đơn hiển thị, snapshot để tra cứu nhanh.</summary>
    [BsonElement("orderCode")]
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Phương thức vận chuyển (Standard / Express).</summary>
    [BsonElement("method")]
    [BsonRepresentation(BsonType.String)]
    public ShippingMethod Method { get; set; } = ShippingMethod.Standard;

    /// <summary>
    /// Phí vận chuyển. REWEAR đang miễn phí vận chuyển toàn bộ nên luôn = 0,
    /// giữ trường để sẵn sàng cho khi chính sách phí ship thay đổi.
    /// </summary>
    [BsonElement("fee")]
    public decimal Fee { get; set; }

    /// <summary>Trạng thái vận chuyển.</summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public ShippingStatus Status { get; set; } = ShippingStatus.Pending;

    /// <summary>Đơn vị vận chuyển (vd: "GHN", "GHTK").</summary>
    [BsonElement("carrier")]
    public string? Carrier { get; set; }

    /// <summary>Mã vận đơn, mirror từ Order.TrackingNumber để tra cứu nhanh.</summary>
    [BsonElement("trackingNumber")]
    public string? TrackingNumber { get; set; }

    /// <summary>Ngày dự kiến giao đến tay khách.</summary>
    [BsonElement("estimatedDeliveryDate")]
    public DateTime? EstimatedDeliveryDate { get; set; }

    /// <summary>Thời điểm bàn giao cho đơn vị vận chuyển.</summary>
    [BsonElement("shippedAt")]
    public DateTime? ShippedAt { get; set; }

    /// <summary>Thời điểm giao thành công.</summary>
    [BsonElement("deliveredAt")]
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Ghi chú vận chuyển (vd: lý do giao thất bại).</summary>
    [BsonElement("note")]
    public string? Note { get; set; }

    // ====== PHÂN BỔ SHIPPER (Task Shipper) ======

    /// <summary>
    /// Id của shipper được phân bổ đơn này. Null nếu đơn chưa được giao cho shipper cụ thể.
    /// </summary>
    [BsonElement("shipperId")]
    public string? ShipperId { get; set; }

    /// <summary>
    /// Tên shipper tại thời điểm phân bổ (snapshot). Khi shipper đổi tên trong hệ thống,
    /// các bản ghi vận chuyển cũ vẫn hiển thị đúng tên lúc họ nhận đơn.
    /// </summary>
    [BsonElement("shipperName")]
    public string? ShipperName { get; set; }

    /// <summary>
    /// Số lần shipper đã giao thất bại cho đơn này. Sau khi đạt ngưỡng (vd: 3 lần),
    /// shipper trả hàng về kho và admin quyết định Reship hoặc hủy vĩnh viễn.
    /// </summary>
    [BsonElement("attemptCount")]
    public int AttemptCount { get; set; } = 0;

    /// <summary>Lý do thất bại của lần giao gần nhất (vd: "Khách không nghe máy").</summary>
    [BsonElement("lastFailureReason")]
    public string? LastFailureReason { get; set; }

    /// <summary>Thời điểm thất bại gần nhất.</summary>
    [BsonElement("lastFailedAt")]
    public DateTime? LastFailedAt { get; set; }

    /// <summary>Thời điểm tạo.</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm cập nhật gần nhất.</summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
