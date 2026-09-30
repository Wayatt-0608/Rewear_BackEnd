using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Đại diện cho một thương hiệu trong hệ thống REWEAR.
/// </summary>
public class Brand
{
    /// <summary>
    /// Khóa chính của thương hiệu.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tên thương hiệu (ví dụ: "Nike", "Adidas", "Levi's").
    /// </summary>
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Slug URL-friendly của thương hiệu (ví dụ: "nike", "new-balance").
    /// </summary>
    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả về thương hiệu.
    /// </summary>
    [BsonElement("description")]
    public string? Description { get; set; }

    /// <summary>
    /// URL logo của thương hiệu.
    /// </summary>
    [BsonElement("logoUrl")]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Trạng thái hoạt động: true = active, false = inactive (soft delete).
    /// </summary>
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thời điểm tạo thương hiệu.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
