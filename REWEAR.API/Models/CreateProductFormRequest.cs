using Microsoft.AspNetCore.Http;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Models;

/// <summary>
/// Request model cho Product Creation với multipart/form-data.
/// Tồn tại trong API layer vì chứa IFormFile (ASP.NET Core dependency).
/// </summary>
public class CreateProductFormRequest
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
    /// Danh sách hình ảnh sản phẩm (1-5 ảnh).
    /// </summary>
    public List<IFormFile> Images { get; set; } = new();
}
