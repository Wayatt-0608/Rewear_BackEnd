using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Đại diện cho một món đồ thời trang secondhand/upcycled trên hệ thống REWEAR.
/// Mỗi Product = 1 unique physical item.
/// </summary>
public class Product
{
    /// <summary>
    /// Khóa chính của sản phẩm.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tiêu đề / tên gọi của sản phẩm.
    /// </summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Slug URL-friendly của sản phẩm.
    /// </summary>
    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết về sản phẩm.
    /// </summary>
    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Id của thương hiệu sản phẩm (reference đến Brand).
    /// </summary>
    [BsonElement("brandId")]
    public string BrandId { get; set; } = string.Empty;

    /// <summary>
    /// Id của danh mục sản phẩm (reference đến Category).
    /// </summary>
    [BsonElement("categoryId")]
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Giá bán (có thể là 0 nếu cho miễn phí).
    /// </summary>
    [BsonElement("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// Số món còn lại trong listing này.
    /// Quần áo secondhand thường là 1 món vật lý, nhưng cho phép seller đăng
    /// nhiều món cùng kiểu (vd: 3 chiếc áo giống nhau) nên cần số tồn thực.
    /// = 1 với hầu hết sản phẩm đơn lẻ.
    /// </summary>
    [BsonElement("stockQuantity")]
    public int StockQuantity { get; set; } = 1;

    /// <summary>
    /// Tình trạng sản phẩm secondhand.
    /// </summary>
    [BsonElement("condition")]
    [BsonRepresentation(BsonType.String)]
    public ProductCondition Condition { get; set; } = ProductCondition.Good;

    /// <summary>
    /// Size của sản phẩm (null nếu không có).
    /// </summary>
    [BsonElement("size")]
    public string? Size { get; set; }

    /// <summary>
    /// Màu sắc của sản phẩm (null nếu không có).
    /// </summary>
    [BsonElement("color")]
    public string? Color { get; set; }

    /// <summary>
    /// Danh sách URL hình ảnh của sản phẩm.
    /// </summary>
    [BsonElement("imageUrls")]
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>
    /// Trạng thái thương mại của sản phẩm.
    /// </summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public ProductStatus Status { get; set; } = ProductStatus.Available;

    /// <summary>
    /// Trạng thái hoạt động: true = active, false = inactive (soft delete).
    /// </summary>
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

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
