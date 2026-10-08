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

    /// <summary>
    /// Chốt (reserve) N món của một sản phẩm bằng atomic update.
    /// Chỉ thành công khi sản phẩm vẫn Available, IsActive và còn đủ tồn kho.
    /// Dùng cho bước Checkout (Task 5) để tránh 2 người cùng mua 1 món.
    /// </summary>
    /// <returns>Số document được update (0 = đã có người khác mua trước / hết hàng).</returns>
    Task<long> TryReserveStockAsync(string productId, int quantity);

    /// <summary>
    /// Trả lại N món về kho khi checkout thất bại hoặc đơn bị hủy.
    /// Chuyển status Sold về Available nếu hết lý do bị bán.
    /// </summary>
    /// <returns>Số document được update.</returns>
    Task<long> ReleaseStockAsync(string productId, int quantity);

    /// <summary>
    /// Hoàn tất bán: ghi nhận đã bán N món (trừ tồn, chuyển status sang Sold nếu hết hàng).
    /// Dùng khi đơn chuyển sang Confirmed / Delivered.
    /// </summary>
    /// <returns>Số document được update.</returns>
    Task<long> CommitStockAsync(string productId, int quantity);

    /// <summary>
    /// Kiểm tra xem URL ảnh có đang được sử dụng bởi sản phẩm khác không.
    /// </summary>
    /// <param name="imageUrl">URL ảnh cần kiểm tra.</param>
    /// <param name="excludeProductId">ProductId cần loại trừ (thường là product hiện tại đang update).</param>
    /// <returns>True nếu URL đang được sử dụng bởi sản phẩm khác.</returns>
    Task<bool> IsImageUrlInUseByOtherProductAsync(string imageUrl, string? excludeProductId = null);
}
