namespace REWEAR.Application.DTOs;

/// <summary>
/// DTO cho yêu cầu tạo danh mục mới.
/// </summary>
public class CreateCategoryRequest
{
    /// <summary>
    /// Tên danh mục (bắt buộc).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả về danh mục (tùy chọn).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// URL hình ảnh đại diện (tùy chọn).
    /// </summary>
    public string? ImageUrl { get; set; }
}

/// <summary>
/// DTO cho yêu cầu cập nhật danh mục.
/// </summary>
public class UpdateCategoryRequest
{
    /// <summary>
    /// Tên danh mục (bắt buộc).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả về danh mục (tùy chọn).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// URL hình ảnh đại diện (tùy chọn).
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Trạng thái hoạt động.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO phản hồi thông tin danh mục.
/// </summary>
public class CategoryResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
