using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Lấy giỏ hàng kèm thông tin sản phẩm đầy đủ
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var cart = await _cartService.GetCartAsync(userId);
        return Ok(new ApiResponse { Success = true, Data = cart });
    }

    /// <summary>
    /// Thêm sản phẩm vào giỏ hàng
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _cartService.AddToCartAsync(userId, request);
        if (!result.Success)
        {
            return result.ErrorCode == ApiErrorCode.NotFound
                ? NotFound(result)
                : BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật số lượng của một mục trong giỏ hàng
    /// </summary>
    [HttpPut("items/{itemId}")]
    public async Task<IActionResult> UpdateItem(string itemId, [FromBody] UpdateCartItemRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _cartService.UpdateItemAsync(userId, itemId, request);
        if (!result.Success)
        {
            // Lỗi "vượt giới hạn số lượng" là Bad Request, không phải Not Found.
            // Chỉ trả 404 khi thật sự không tìm thấy mục trong giỏ.
            return result.ErrorCode == ApiErrorCode.NotFound
                ? NotFound(result)
                : BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Tick / bỏ tick một dòng giỏ hàng
    /// </summary>
    [HttpPut("items/{itemId}/select")]
    public async Task<IActionResult> SelectItem(string itemId, [FromBody] SelectCartItemRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _cartService.SelectItemAsync(userId, itemId, request);

        if (!result.Success)
            return result.ErrorCode == ApiErrorCode.NotFound
                ? NotFound(result)
                : BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Tick / bỏ tick toàn bộ giỏ hàng
    /// </summary>
    [HttpPut("items/select-all")]
    public async Task<IActionResult> SelectAllItems([FromBody] SelectAllCartItemsRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _cartService.SelectAllItemsAsync(userId, request);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Xóa một mục khỏi giỏ hàng
    /// </summary>
    [HttpDelete("items/{itemId}")]
    public async Task<IActionResult> RemoveItem(string itemId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _cartService.RemoveItemAsync(userId, itemId);
        if (!result.Success)
            return NotFound(result);

        return Ok(result);
    }

    /// <summary>
    /// Xóa toàn bộ giỏ hàng
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> ClearCart()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _cartService.ClearCartAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Lấy số lượng mục trong giỏ hàng
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var count = await _cartService.GetCartItemCountAsync(userId);
        return Ok(new ApiResponse { Success = true, Data = new { Count = count } });
    }
}
