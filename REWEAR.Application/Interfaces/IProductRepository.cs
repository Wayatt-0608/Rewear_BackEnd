using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Product Repository.
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Lấy tất cả sản phẩm.
    /// </summary>
    Task<List<Product>> GetAllAsync();

    /// <summary>
    /// Lấy tất cả sản phẩm với filter theo trạng thái IsActive.
    /// </summary>
    /// <param name="isActive">null = all, true = active only, false = inactive only</param>
    Task<List<Product>> GetAllAsync(bool? isActive);

    /// <summary>
    /// Lấy tất cả sản phẩm với query parameters (filter ở MongoDB).
    /// </summary>
    Task<List<Product>> GetAllAsync(ProductQueryParameters query);

    /// <summary>
    /// Lấy sản phẩm theo Id.
    /// </summary>
    Task<Product?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy sản phẩm theo slug.
    /// </summary>
    Task<Product?> GetBySlugAsync(string slug);

    /// <summary>
    /// Tạo sản phẩm mới.
    /// </summary>
    Task CreateAsync(Product product);

    /// <summary>
    /// Cập nhật sản phẩm.
    /// </summary>
    Task UpdateAsync(Product product);
}
