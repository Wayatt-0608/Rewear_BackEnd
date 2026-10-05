using Microsoft.AspNetCore.Http;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Models;

/// <summary>
/// Form model cho customer tạo sourcing request (multipart/form-data).
/// IFormFile CHỈ nằm ở API layer.
/// </summary>
public class CreateSourcingFormRequest
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
    /// Hình ảnh món đồ (1-5 ảnh).
    /// </summary>
    public List<IFormFile> Images { get; set; } = new();
}
