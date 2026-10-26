using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service xử lý danh sách yêu thích (Wishlist) của người dùng.
/// </summary>
/// <remarks>
/// Mọi thao tác đều dựa trên userId lấy từ JWT, KHÔNG nhận userId từ request,
/// nên user chỉ thao tác được trên wishlist của chính mình.
/// </remarks>
public interface IWishlistService
{
    /// <summary>
    /// Lấy danh sách wishlist của user (có phân trang, join Product theo batch).
    /// </summary>
    Task<PagedWishlistResponse> GetMyWishlistAsync(string userId, WishlistQueryParameters query);

    /// <summary>
    /// Thêm sản phẩm vào wishlist. Idempotent (thêm trùng vẫn trả thành công).
    /// </summary>
    Task<ApiResponse> AddAsync(string userId, string productId);

    /// <summary>
    /// Xóa sản phẩm khỏi wishlist. Idempotent (không có cũng trả thành công).
    /// </summary>
    Task<ApiResponse> RemoveAsync(string userId, string productId);

    /// <summary>
    /// Kiểm tra sản phẩm có nằm trong wishlist của user không.
    /// </summary>
    Task<WishlistCheckResponse> CheckAsync(string userId, string productId);
}
