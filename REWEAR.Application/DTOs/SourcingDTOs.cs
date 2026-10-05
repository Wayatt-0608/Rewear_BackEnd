using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

/// <summary>
/// DTO cho customer tạo sourcing request.
/// KHÔNG chứa IFormFile - image upload xử lý ở Controller.
/// </summary>
public class CreateSourcingRequest
{
    /// <summary>
    /// Tiêu đề món đồ muốn bán.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Brand ID (phải tồn tại và active).
    /// </summary>
    public string BrandId { get; set; } = string.Empty;

    /// <summary>
    /// Category ID (phải tồn tại và active).
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Tình trạng món đồ do customer khai báo.
    /// </summary>
    public ProductCondition DeclaredCondition { get; set; } = ProductCondition.Good;

    /// <summary>
    /// Size của món đồ.
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// Màu sắc món đồ.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Giá customer mong muốn bán (optional, phải > 0 nếu có).
    /// </summary>
    public decimal? CustomerExpectedPrice { get; set; }

    /// <summary>
    /// Danh sách URL hình ảnh đã upload lên Cloudinary.
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>
    /// Danh sách Cloudinary PublicId của ảnh (dùng cho cleanup/rollback).
    /// </summary>
    public List<string> ImagePublicIds { get; set; } = new();
}

/// <summary>
/// DTO phản hồi thông tin sourcing request.
/// </summary>
public class SourcingResponse
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public ProductCondition DeclaredCondition { get; set; }
    public ProductCondition? InspectedCondition { get; set; }
    public decimal? CustomerExpectedPrice { get; set; }
    public decimal? OfferedPrice { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public SourcingStatus Status { get; set; }
    public string? AdminNote { get; set; }
    public string? RejectReason { get; set; }
    public string? ProductId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// DTO cho Staff/Admin định giá sourcing request.
/// </summary>
public class OfferSourcingRequest
{
    /// <summary>
    /// Giá REWEAR đề nghị thu mua (phải > 0).
    /// </summary>
    public decimal OfferedPrice { get; set; }

    /// <summary>
    /// Ghi chú nội bộ từ Admin/Staff.
    /// </summary>
    public string? AdminNote { get; set; }
}

/// <summary>
/// DTO cho Staff/Admin từ chối sourcing request.
/// </summary>
public class RejectSourcingRequest
{
    /// <summary>
    /// Lý do từ chối (bắt buộc).
    /// </summary>
    public string RejectReason { get; set; } = string.Empty;
}

/// <summary>
/// DTO cho Staff/Admin kiểm định sourcing request.
/// </summary>
public class InspectSourcingRequest
{
    /// <summary>
    /// Tình trạng chính thức sau kiểm định của REWEAR.
    /// </summary>
    public ProductCondition InspectedCondition { get; set; }

    /// <summary>
    /// Ghi chú nội bộ từ Admin/Staff.
    /// </summary>
    public string? AdminNote { get; set; }
}

/// <summary>
/// DTO cho Staff/Admin convert sourcing thành Product.
/// </summary>
public class ConvertToProductRequest
{
    /// <summary>
    /// Giá bán ra cho customer (phải > 0).
    /// KHÔNG phải OfferedPrice (giá thu mua).
    /// </summary>
    public decimal SellingPrice { get; set; }
}
