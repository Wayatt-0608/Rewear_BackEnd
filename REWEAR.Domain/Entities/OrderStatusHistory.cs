using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Một mốc thay đổi trạng thái của đơn hàng (Task 7 - Order timeline).
/// Mỗi lần đổi trạng thái sẽ INSERT 1 document mới, không sửa document cũ,
/// để giữ được lịch sử đầy đủ cho người mua và đội vận chuyển.
/// </summary>
[BsonIgnoreExtraElements]
public class OrderStatusHistory
{
    /// <summary>Khóa chính của mốc lịch sử.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Id đơn hàng liên quan.</summary>
    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Trạng thái trước khi đổi (null = đơn vừa tạo).</summary>
    [BsonElement("fromStatus")]
    [BsonRepresentation(BsonType.String)]
    public OrderStatus? FromStatus { get; set; }

    /// <summary>Trạng thái sau khi đổi.</summary>
    [BsonElement("toStatus")]
    [BsonRepresentation(BsonType.String)]
    public OrderStatus ToStatus { get; set; }

    /// <summary>Ghi chú thêm cho mốc này (vd: lý do hủy, số vận đơn).</summary>
    [BsonElement("note")]
    public string? Note { get; set; }

    /// <summary>Ai thực hiện thay đổi (Customer / Staff / System / Shipper).</summary>
    [BsonElement("changedBy")]
    [BsonRepresentation(BsonType.String)]
    public OrderStatusChangedBy ChangedBy { get; set; } = OrderStatusChangedBy.System;

    /// <summary>Id người dùng thao tác (null nếu là System).</summary>
    [BsonElement("changedByUserId")]
    public string? ChangedByUserId { get; set; }

    /// <summary>
    /// Tên người thao tác tại thời điểm đổi trạng thái (snapshot).
    /// FE dùng để hiển thị "Nguyễn Văn A đã xác nhận đơn" thay vì chỉ show id.
    /// </summary>
    [BsonElement("changedByName")]
    public string? ChangedByName { get; set; }

    /// <summary>Thời điểm thay đổi.</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
