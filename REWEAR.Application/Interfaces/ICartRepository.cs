using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Cart Repository.
/// </summary>
public interface ICartRepository
{
    /// <summary>
    /// Lấy giỏ hàng theo userId.
    /// </summary>
    Task<Cart?> GetByUserIdAsync(string userId);

    /// <summary>
    /// Tạo giỏ hàng mới.
    /// </summary>
    Task CreateAsync(Cart cart);

    /// <summary>
    /// Cập nhật giỏ hàng (ghi đè toàn bộ document).
    /// </summary>
    Task UpdateAsync(Cart cart);

    /// <summary>
    /// Xóa giỏ hàng theo userId.
    /// </summary>
    Task DeleteByUserIdAsync(string userId);

    /// <summary>
    /// Làm mới hạn sử dụng của giỏ hàng (đẩy ExpiresAt ra xa thêm 30 ngày).
    /// </summary>
    Task RefreshExpirationAsync(string userId);
}
