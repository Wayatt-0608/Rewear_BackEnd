using REWEAR.Application.DTOs;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Product Service.
/// </summary>
public interface IProductService
{
    /// <summary>
    /// Lấy tất cả sản phẩm.
    /// </summary>
    Task<List<ProductResponse>> GetAllAsync();

    /// <summary>
    /// Lấy tất cả sản phẩm với filter theo trạng thái IsActive.
    /// </summary>
    /// <param name="isActive">null = all, true = active only, false = inactive only</param>
    Task<List<ProductResponse>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Lấy tất cả sản phẩm với query parameters (filter/search).
    /// </summary>
    Task<List<ProductResponse>> GetAllAsync(ProductQueryParameters query);

    /// <summary>
    /// Lấy sản phẩm theo Id.
    /// </summary>
    Task<ProductResponse?> GetByIdAsync(string id);

    /// <summary>
    /// Tạo sản phẩm mới.
    /// </summary>
    Task<ProductResponse> CreateAsync(CreateProductRequest request);

    /// <summary>
    /// Cập nhật sản phẩm.
    /// </summary>
    Task<ProductResponse?> UpdateAsync(string id, UpdateProductRequest request);

    /// <summary>
    /// Xóa sản phẩm (soft delete - đặt IsActive = false).
    /// </summary>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// Khôi phục sản phẩm đã bị soft delete (đặt IsActive = true).
    /// </summary>
    Task<ProductResponse?> RestoreAsync(string id);

    /// <summary>
    /// Cập nhật trạng thái thương mại của sản phẩm.
    /// </summary>
    Task<ProductResponse?> UpdateStatusAsync(string id, ProductStatus status);

    /// <summary>
    /// Cập nhật sản phẩm với xử lý ảnh nâng cao.
    /// </summary>
    /// <param name="id">Product ID</param>
    /// <param name="request">Update request</param>
    /// <param name="newImageUrls">Danh sách URL ảnh mới đã upload</param>
    /// <param name="keepImageUrls">Danh sách URL ảnh cũ cần giữ lại</param>
    /// <param name="clearAllImages">Cờ xóa tất cả ảnh cũ</param>
    Task<ProductResponse?> UpdateWithImagesAsync(
        string id,
        UpdateProductRequest request,
        List<string> newImageUrls,
        List<string>? keepImageUrls,
        bool clearAllImages);

    /// <summary>
    /// Kiểm tra ảnh cũ có cần xóa không.
    /// </summary>
    /// <param name="productId">Product ID</param>
    /// <param name="currentImageUrls">Danh sách URL ảnh hiện tại sau khi update</param>
    /// <returns>Danh sách URL ảnh không còn sử dụng và có thể xóa</returns>
    Task<List<string>> GetUnusedImagesToDeleteAsync(string productId, List<string> currentImageUrls);
}
