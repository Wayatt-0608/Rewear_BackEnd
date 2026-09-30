using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Brand Service.
/// </summary>
public interface IBrandService
{
    /// <summary>
    /// Lấy tất cả thương hiệu.
    /// </summary>
    Task<List<BrandResponse>> GetAllAsync();

    /// <summary>
    /// Lấy tất cả thương hiệu với filter theo trạng thái IsActive.
    /// </summary>
    /// <param name="isActive">null = all, true = active only, false = inactive only</param>
    Task<List<BrandResponse>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Lấy thương hiệu theo Id.
    /// </summary>
    Task<BrandResponse?> GetByIdAsync(string id);

    /// <summary>
    /// Tạo thương hiệu mới.
    /// </summary>
    Task<BrandResponse> CreateAsync(CreateBrandRequest request);

    /// <summary>
    /// Cập nhật thương hiệu.
    /// </summary>
    Task<BrandResponse?> UpdateAsync(string id, UpdateBrandRequest request);

    /// <summary>
    /// Xóa thương hiệu (soft delete - đặt IsActive = false).
    /// </summary>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// Khôi phục thương hiệu đã bị soft delete (đặt IsActive = true).
    /// </summary>
    Task<BrandResponse?> RestoreAsync(string id);
}
