using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

/// <summary>
/// API danh sách yêu thích (Wishlist) của người dùng.
/// </summary>
/// <remarks>
/// <para>
/// Yêu cầu đăng nhập, không giới hạn role: mọi role đều có "wishlist của tôi",
/// đồng bộ với <see cref="VouchersController"/> (dùng policy Member cũ từng gây 403
/// cho Admin/Staff/Shipper).
/// </para>
/// <para>
/// userId LUÔN lấy từ JWT (<c>ClaimTypes.NameIdentifier</c>), không bao giờ nhận từ
/// request, nên user chỉ thao tác được trên wishlist của chính mình.
/// </para>
/// </remarks>
[ApiController]
[Route("api/wishlist")]
[Authorize]
[TypeFilter(typeof(REWEAR.API.Filters.ObjectIdRouteFilter))]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlistService;

    public WishlistController(IWishlistService wishlistService)
    {
        _wishlistService = wishlistService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // DANH SÁCH WISHLIST
    // ============================================

    /// <summary>
    /// Lấy danh sách sản phẩm đã yêu thích của tôi, mới nhất trước, có phân trang.
    /// </summary>
    /// <param name="query">
    /// page = trang hiện tại (bắt đầu từ 1);
    /// pageSize = số bản ghi mỗi trang (tối đa 100).
    /// </param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyWishlist([FromQuery] WishlistQueryParameters query)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var wishlist = await _wishlistService.GetMyWishlistAsync(userId, query);
        return Ok(new ApiResponse { Success = true, Data = wishlist });
    }

    // ============================================
    // THÊM / XÓA
    // ============================================

    /// <summary>
    /// Thêm sản phẩm vào danh sách yêu thích của tôi.
    /// </summary>
    /// <remarks>
    /// Idempotent: thêm trùng vẫn trả 200 và KHÔNG tạo bản ghi trùng.
    /// Trả 404 nếu sản phẩm không tồn tại, 400 nếu sản phẩm không còn khả dụng.
    /// </remarks>
    [HttpPost("{productId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Add(string productId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _wishlistService.AddAsync(userId, productId);

        if (!result.Success)
        {
            return result.ErrorCode == ApiErrorCode.NotFound
                ? NotFound(result)
                : BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Xóa sản phẩm khỏi danh sách yêu thích của tôi.
    /// </summary>
    /// <remarks>
    /// Idempotent: xóa sản phẩm không có trong wishlist vẫn trả 200.
    /// </remarks>
    [HttpDelete("{productId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove(string productId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _wishlistService.RemoveAsync(userId, productId);
        return Ok(result);
    }

    // ============================================
    // KIỂM TRA
    // ============================================

    /// <summary>
    /// Kiểm tra 1 sản phẩm có nằm trong danh sách yêu thích của tôi không.
    /// </summary>
    [HttpGet("check/{productId}")]
    [ProducesResponseType(typeof(WishlistCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Check(string productId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _wishlistService.CheckAsync(userId, productId);
        return Ok(new ApiResponse { Success = true, Data = result });
    }
}
