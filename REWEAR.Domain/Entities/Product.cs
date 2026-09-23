using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Đại diện cho một món đồ thời trang được đăng bán / trao đổi trên hệ thống REWEAR.
/// Mỗi sản phẩm thuộc về một người dùng (OwnerId) và có thể được nhiều người khác yêu thích.
/// </summary>
public class Product
{
    /// <summary>
    /// Khóa chính của sản phẩm, MongoDB sẽ tự tạo ObjectId khi insert.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tiêu đề / tên gọi của sản phẩm (ví dụ: "Áo thun trắng cổ tròn").
    /// </summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết về sản phẩm: tình trạng, kích thước, chất liệu...
    /// </summary>
    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Giá bán (có thể là 0 nếu cho miễn phí / trao đổi).
    /// </summary>
    [BsonElement("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// Danh mục sản phẩm: Áo, Quần, Váy, Phụ kiện...
    /// </summary>
    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Tình trạng sản phẩm: "Mới", "Như mới", "Đã sử dụng"...
    /// </summary>
    [BsonElement("condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách URL hình ảnh của sản phẩm.
    /// </summary>
    [BsonElement("imageUrls")]
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>
    /// Id của người đăng bán sản phẩm này.
    /// </summary>
    [BsonElement("ownerId")]
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái sản phẩm: "Còn hàng", "Đã bán", "Đã đặt cọc"...
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Còn hàng";

    /// <summary>
    /// Thời điểm tạo sản phẩm.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
