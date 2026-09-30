using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Brand Repository.
/// </summary>
public interface IBrandRepository
{
    /// <summary>
    /// Lấy tất cả thương hiệu.
    /// </summary>
    Task<List<Brand>> GetAllAsync();

    /// <summary>
    /// Lấy tất cả thương hiệu với filter theo trạng thái IsActive.
    /// </summary>
    /// <param name="isActive">null = all, true = active only, false = inactive only</param>
    Task<List<Brand>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Lấy thương hiệu theo Id.
    /// </summary>
    Task<Brand?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy thương hiệu theo tên (case-insensitive).
    /// </summary>
    Task<Brand?> GetByNameAsync(string name);

    /// <summary>
    /// Tạo thương hiệu mới.
    /// </summary>
    Task CreateAsync(Brand brand);

    /// <summary>
    /// Cập nhật thương hiệu.
    /// </summary>
    Task UpdateAsync(Brand brand);
}
