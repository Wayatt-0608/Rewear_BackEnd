using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Đại diện cho yêu cầu thu mua đồ cũ (Sourcing Request) trong hệ thống REWEAR.
/// Khi customer muốn bán đồ secondhand cho REWEAR.
/// </summary>
[BsonIgnoreExtraElements]
public class SourcingRequest
{
    /// <summary>
    /// Khóa chính của request.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id của user tạo request.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Tên/mô tả món đồ customer muốn bán.
    /// </summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết món đồ.
    /// </summary>
    [BsonElement("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Id của thương hiệu (reference đến Brand).
    /// </summary>
    [BsonElement("brandId")]
    public string BrandId { get; set; } = string.Empty;

    /// <summary>
    /// Id của danh mục (reference đến Category).
    /// </summary>
    [BsonElement("categoryId")]
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Size của món đồ.
    /// </summary>
    [BsonElement("size")]
    public string? Size { get; set; }

    /// <summary>
    /// Màu sắc của món đồ.
    /// </summary>
    [BsonElement("color")]
    public string? Color { get; set; }

    /// <summary>
    /// Tình trạng món đồ do customer khai báo.
    /// </summary>
    [BsonElement("declaredCondition")]
    [BsonRepresentation(BsonType.String)]
    public ProductCondition DeclaredCondition { get; set; } = ProductCondition.Good;

    /// <summary>
    /// Tình trạng món đồ do REWEAR đánh giá sau khi nhận hàng.
    /// </summary>
    [BsonElement("inspectedCondition")]
    [BsonRepresentation(BsonType.String)]
    public ProductCondition? InspectedCondition { get; set; }

    /// <summary>
    /// Giá customer mong muốn bán (không bắt buộc).
    /// </summary>
    [BsonElement("customerExpectedPrice")]
    public decimal? CustomerExpectedPrice { get; set; }

    /// <summary>
    /// Giá REWEAR đề nghị thu mua (Admin điền sau khi review).
    /// </summary>
    [BsonElement("offeredPrice")]
    public decimal? OfferedPrice { get; set; }

    /// <summary>
    /// Danh sách URL hình ảnh món đồ.
    /// </summary>
    [BsonElement("imageUrls")]
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>
    /// Danh sách Cloudinary PublicId của ảnh (dùng cho cleanup/rollback).
    /// </summary>
    [BsonElement("imagePublicIds")]
    public List<string> ImagePublicIds { get; set; } = new();

    /// <summary>
    /// Trạng thái hiện tại của request.
    /// </summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public SourcingStatus Status { get; set; } = SourcingStatus.Pending;

    /// <summary>
    /// Ghi chú từ Admin/Staff (không hiển thị cho customer).
    /// </summary>
    [BsonElement("adminNote")]
    public string? AdminNote { get; set; }

    /// <summary>
    /// Lý do từ chối (nếu Rejected hoặc Declined).
    /// </summary>
    [BsonElement("rejectReason")]
    public string? RejectReason { get; set; }

    /// <summary>
    /// Id của Product được tạo sau khi convert (nếu Completed).
    /// </summary>
    [BsonElement("productId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ProductId { get; set; }

    /// <summary>
    /// Thời điểm tạo request.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm hoàn thành (Completed hoặc Rejected/Declined).
    /// </summary>
    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }
}
