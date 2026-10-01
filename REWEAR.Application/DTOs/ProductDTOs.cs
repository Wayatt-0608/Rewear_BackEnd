using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

/// <summary>
/// DTO cho yêu cầu tạo sản phẩm mới.
/// </summary>
public class CreateProductRequest
{
    /// <summary>
    /// Tiêu đề sản phẩm (bắt buộc).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết sản phẩm.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Id của thương hiệu (bắt buộc).
    /// </summary>
    public string BrandId { get; set; } = string.Empty;

    /// <summary>
    /// Id của danh mục (bắt buộc).
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Giá bán (phải > 0).
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Tình trạng sản phẩm secondhand.
    /// </summary>
    public ProductCondition Condition { get; set; } = ProductCondition.Good;

    /// <summary>
    /// Size của sản phẩm (tùy chọn).
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// Màu sắc sản phẩm (tùy chọn).
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Danh sách URL hình ảnh.
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();
}

/// <summary>
/// DTO cho yêu cầu cập nhật sản phẩm.
/// </summary>
public class UpdateProductRequest
{
    /// <summary>
    /// Tiêu đề sản phẩm (bắt buộc).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết sản phẩm.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Id của thương hiệu (bắt buộc).
    /// </summary>
    public string BrandId { get; set; } = string.Empty;

    /// <summary>
    /// Id của danh mục (bắt buộc).
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Giá bán (phải > 0).
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Tình trạng sản phẩm secondhand.
    /// </summary>
    public ProductCondition Condition { get; set; } = ProductCondition.Good;

    /// <summary>
    /// Size của sản phẩm (tùy chọn).
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// Màu sắc sản phẩm (tùy chọn).
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Danh sách URL hình ảnh.
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();
}

/// <summary>
/// DTO cho yêu cầu cập nhật trạng thái sản phẩm.
/// </summary>
public class UpdateProductStatusRequest
{
    /// <summary>
    /// Trạng thái thương mại mới.
    /// </summary>
    public ProductStatus Status { get; set; }
}

/// <summary>
/// DTO phản hồi thông tin sản phẩm.
/// </summary>
public class ProductResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string BrandId { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public ProductCondition Condition { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public ProductStatus Status { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO cho query parameters khi lấy danh sách sản phẩm.
/// </summary>
public class ProductQueryParameters
{
    /// <summary>
    /// Filter theo trạng thái hoạt động: null = all, true = active, false = inactive.
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Filter theo Brand Id.
    /// </summary>
    public string? BrandId { get; set; }

    /// <summary>
    /// Filter theo Category Id.
    /// </summary>
    public string? CategoryId { get; set; }

    /// <summary>
    /// Filter theo tình trạng sản phẩm secondhand.
    /// </summary>
    public ProductCondition? Condition { get; set; }

    /// <summary>
    /// Filter theo trạng thái thương mại.
    /// </summary>
    public ProductStatus? Status { get; set; }

    /// <summary>
    /// Giá tối thiểu (inclusive).
    /// </summary>
    public decimal? MinPrice { get; set; }

    /// <summary>
    /// Giá tối đa (inclusive).
    /// </summary>
    public decimal? MaxPrice { get; set; }

    /// <summary>
    /// Search keyword (tìm trong Title và Description, case-insensitive).
    /// </summary>
    public string? Search { get; set; }
}
