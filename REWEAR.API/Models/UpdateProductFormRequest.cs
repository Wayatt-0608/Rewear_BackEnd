using Microsoft.AspNetCore.Http;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Models;

/// <summary>
/// Request model cho Product Update voi multipart/form-data.
/// Ho tro upload anh moi va quan ly anh cu.
/// </summary>
public class UpdateProductFormRequest
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
    /// Số món còn lại (phải >= 1).
    /// </summary>
    public int StockQuantity { get; set; } = 1;

    /// <summary>
    /// Danh sách ảnh mới cần upload (1-5 ảnh).
    /// Nếu null hoặc rỗng → không upload ảnh mới.
    /// </summary>
    public List<IFormFile>? Images { get; set; }

    /// <summary>
    /// Danh sách URL ảnh cũ CẦN GIỮ LẠI.
    /// - Nếu Images có giá trị: kết hợp với ảnh mới upload
    /// - Nếu Images == null: chỉ giữ các URL trong danh sách này
    /// - Nếu Images == null và KeepImageUrls == null: giữ nguyên ảnh cũ
    /// </summary>
    public List<string>? KeepImageUrls { get; set; }

    /// <summary>
    /// Cờ xóa toàn bộ ảnh cũ (chỉ khi business rule cho phép).
    /// Khi = true: xóa tất cả ảnh cũ và chỉ giữ ảnh mới upload.
    /// </summary>
    public bool ClearAllImages { get; set; } = false;
}
