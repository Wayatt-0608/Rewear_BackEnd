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
}
