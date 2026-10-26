using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho WishlistItem - liên kết User ↔ Product trong danh sách yêu thích.
/// </summary>
/// <remarks>
/// Mỗi (user, product) có đúng 1 bản ghi. Ràng buộc này được MongoDB đảm bảo bằng
/// unique compound index (userId, productId) trên collection "WishlistItems", nên
/// 2 request thêm đồng thời vẫn chỉ tạo 1 bản ghi (request thua sẽ nhận
/// MongoWriteException code 11000 và được service xử lý idempotent).
/// </remarks>
public interface IWishlistRepository
{
    /// <summary>
    /// Lấy 1 dòng wishlist theo (user, product).
    /// </summary>
    Task<WishlistItem?> GetByUserAndProductAsync(string userId, string productId);

    /// <summary>
    /// Kiểm tra (user, product) đã tồn tại trong wishlist chưa.
    /// </summary>
    Task<bool> ExistsAsync(string userId, string productId);

    /// <summary>
    /// Thêm 1 dòng wishlist mới.
    /// </summary>
    /// <remarks>
    /// Ném <c>MongoWriteException</c> với WriteError.Code = 11000 nếu (user, product)
    /// đã tồn tại do request chạy song song. Service bắt lỗi này để trả về idempotent.
    /// </remarks>
    Task AddAsync(WishlistItem item);

    /// <summary>
    /// Xóa dòng wishlist theo (user, product).
    /// </summary>
    /// <returns>True nếu có bản ghi bị xóa, false nếu không tồn tại (idempotent).</returns>
    Task<bool> RemoveAsync(string userId, string productId);

    /// <summary>
    /// Lấy danh sách wishlist của 1 user, có phân trang, mới nhất trước.
    /// </summary>
    Task<(List<WishlistItem> Items, long TotalCount)> GetPagedByUserAsync(string userId, int page, int pageSize);
}
