using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Đại diện cho một danh mục sản phẩm trong hệ thống REWEAR.
/// </summary>
public class Category
{
    /// <summary>
    /// Khóa chính của danh mục.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tên danh mục (ví dụ: "Tops", "Bottoms", "Dresses").
    /// </summary>
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Slug URL-friendly của danh mục (ví dụ: "tops", "vintage-tops").
    /// </summary>
    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả về danh mục.
    /// </summary>
    [BsonElement("description")]
    public string? Description { get; set; }

    /// <summary>
    /// URL hình ảnh đại diện cho danh mục.
    /// </summary>
    [BsonElement("imageUrl")]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Trạng thái hoạt động: true = active, false = inactive (soft delete).
    /// </summary>
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thời điểm tạo danh mục.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
