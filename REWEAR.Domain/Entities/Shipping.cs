using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Thông tin vận chuyển của một đơn hàng (Task 9).
/// Tách khỏi Order vì vận chuyển có vòng đời riêng (chuẩn bị giao, đang chuyển,
/// giao xong) và cần tra cứu độc lập cho dashboard shipper.
/// </summary>
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

    /// <summary>Thời điểm tạo.</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm cập nhật gần nhất.</summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}