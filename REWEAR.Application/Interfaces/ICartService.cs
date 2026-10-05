using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Cart Service.
/// </summary>
public interface ICartService
{
    /// <summary>
    /// Lấy giỏ hàng kèm thông tin sản phẩm mới nhất.
    /// </summary>
    Task<CartResponse> GetCartAsync(string userId);

    /// <summary>
    /// Thêm sản phẩm vào giỏ.
    /// </summary>
    Task<ApiResponse> AddToCartAsync(string userId, AddToCartRequest request);

    /// <summary>
    /// Cập nhật số lượng của một dòng trong giỏ.
    /// </summary>
    Task<ApiResponse> UpdateItemAsync(string userId, string itemId, UpdateCartItemRequest request);

    /// <summary>
    /// Tick / bỏ tick một dòng giỏ hàng (quyết định dòng đó có được mua khi checkout).
    /// </summary>
    Task<ApiResponse> SelectItemAsync(string userId, string itemId, SelectCartItemRequest request);

    /// <summary>
    /// Tick / bỏ tick toàn bộ giỏ hàng cùng lúc.
    /// </summary>
    Task<ApiResponse> SelectAllItemsAsync(string userId, SelectAllCartItemsRequest request);

    /// <summary>
    /// Xóa một dòng khỏi giỏ.
    /// </summary>
    Task<ApiResponse> RemoveItemAsync(string userId, string itemId);

    /// <summary>
    /// Xóa toàn bộ giỏ hàng.
    /// </summary>
    Task<ApiResponse> ClearCartAsync(string userId);

    /// <summary>
    /// Số lượng sản phẩm trong giỏ (dùng cho badge trên icon giỏ).
    /// </summary>
    Task<int> GetCartItemCountAsync(string userId);
}
