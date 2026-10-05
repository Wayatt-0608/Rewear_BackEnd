using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Giỏ hàng của người dùng.
/// Mỗi user có đúng 1 giỏ, lưu chung 1 document chứa mảng Items.
/// </summary>
[BsonIgnoreExtraElements]
public class Cart
{
    /// <summary>
    /// Số ngày giỏ hàng được giữ lại trước khi tự động hết hạn.
    /// </summary>
    public const int EXPIRATION_DAYS = 30;

    /// <summary>
    /// Khóa chính.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id của chủ giỏ hàng.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách sản phẩm trong giỏ.
    /// </summary>
    [BsonElement("items")]
    public List<CartItem> Items { get; set; } = new();

    /// <summary>
    /// Thời điểm tạo giỏ.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất (được làm mới mỗi khi giỏ thay đổi).
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm giỏ hàng hết hạn. MongoDB TTL index sẽ tự xóa document
    /// sau thời điểm này (dọn dẹp giỏ bị bỏ quên).
    /// </summary>
    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(EXPIRATION_DAYS);

    /// <summary>
    /// Tổng số lượng sản phẩm trong giỏ (không lưu xuống DB, tính lúc đọc).
    /// </summary>
    [BsonIgnore]
    public int TotalQuantity => Items.Sum(i => i.Quantity);

    /// <summary>
    /// Tổng tiền theo giá chụp lúc thêm vào giỏ (không lưu xuống DB).
    /// </summary>
    [BsonIgnore]
    public decimal SubTotal => Items.Sum(i => i.LineTotal);

    /// <summary>
    /// Giỏ đã hết hạn chưa.
    /// </summary>
    [BsonIgnore]
    public bool IsExpired => ExpiresAt <= DateTime.UtcNow;
}
