using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Category Repository.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Lấy tất cả danh mục.
    /// </summary>
    Task<List<Category>> GetAllAsync();

    /// <summary>
    /// Lấy tất cả danh mục với filter theo trạng thái IsActive.
    /// </summary>
    /// <param name="isActive">null = all, true = active only, false = inactive only</param>
    Task<List<Category>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Lấy danh mục theo Id.
    /// </summary>
    Task<Category?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy danh mục theo tên (case-insensitive).
    /// </summary>
    Task<Category?> GetByNameAsync(string name);

    /// <summary>
    /// Tạo danh mục mới.
    /// </summary>
    Task CreateAsync(Category category);

    /// <summary>
    /// Cập nhật danh mục.
    /// </summary>
    Task UpdateAsync(Category category);
}
