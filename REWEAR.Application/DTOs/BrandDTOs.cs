namespace REWEAR.Application.DTOs;

/// <summary>
/// DTO cho yêu cầu tạo thương hiệu mới.
/// </summary>
public class CreateBrandRequest
{
    /// <summary>
    /// Tên thương hiệu (bắt buộc).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả về thương hiệu (tùy chọn).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// URL logo của thương hiệu (tùy chọn).
    /// </summary>
    public string? LogoUrl { get; set; }
}

/// <summary>
/// DTO cho yêu cầu cập nhật thương hiệu.
/// </summary>
public class UpdateBrandRequest
{
    /// <summary>
    /// Tên thương hiệu (bắt buộc).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả về thương hiệu (tùy chọn).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// URL logo của thương hiệu (tùy chọn).
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Trạng thái hoạt động.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO phản hồi thông tin thương hiệu.
/// </summary>
public class BrandResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
