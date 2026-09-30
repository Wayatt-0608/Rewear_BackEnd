using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Category Service.
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Lấy tất cả danh mục.
    /// </summary>
    Task<List<CategoryResponse>> GetAllAsync();

    /// <summary>
    /// Lấy tất cả danh mục với filter theo trạng thái IsActive.
    /// </summary>
    /// <param name="isActive">null = all, true = active only, false = inactive only</param>
    Task<List<CategoryResponse>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Lấy danh mục theo Id.
    /// </summary>
    Task<CategoryResponse?> GetByIdAsync(string id);

    /// <summary>
    /// Tạo danh mục mới.
    /// </summary>
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request);

    /// <summary>
    /// Cập nhật danh mục.
    /// </summary>
    Task<CategoryResponse?> UpdateAsync(string id, UpdateCategoryRequest request);

    /// <summary>
    /// Xóa danh mục (soft delete - đặt IsActive = false).
    /// </summary>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// Khôi phục danh mục đã bị soft delete (đặt IsActive = true).
    /// </summary>
    Task<CategoryResponse?> RestoreAsync(string id);
}
