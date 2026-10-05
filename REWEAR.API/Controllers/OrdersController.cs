using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // CHECKOUT (Task 5)
    // ============================================

    /// <summary>
    /// Đặt hàng từ giỏ hàng hiện tại
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _orderService.CheckoutAsync(userId, request);

        if (result.Success)
            return Ok(result);

        // 404 khi không tìm thấy địa chỉ, 409 khi có xung đột tồn kho,
        // còn lại (giỏ rỗng, giỏ sai) là 400.
        return result.ErrorCode switch
        {
            ApiErrorCode.NotFound => NotFound(result),
            ApiErrorCode.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }

    // ============================================
    // ORDER MANAGEMENT (Task 6)
    // ============================================

    /// <summary>
    /// Lấy danh sách đơn hàng của người dùng hiện tại
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyOrders([FromQuery] OrderQueryParameters query)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var orders = await _orderService.GetOrdersByUserAsync(userId, query);

        return Ok(new ApiResponse
        {
            Success = true,
            Data = orders
        });
    }

    /// <summary>
    /// Lấy chi tiết một đơn hàng của người dùng hiện tại
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrderDetail(string id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var order = await _orderService.GetOrderDetailAsync(userId, id);
        if (order == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        return Ok(new ApiResponse
        {
            Success = true,
            Data = order
        });
    }

    /// <summary>
    /// Lấy chi tiết đơn hàng bằng mã đơn
    /// </summary>
    [HttpGet("code/{orderCode}")]
    public async Task<IActionResult> GetOrderByCode(string orderCode)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var order = await _orderService.GetOrderDetailByCodeAsync(userId, orderCode);
        if (order == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        return Ok(new ApiResponse
        {
            Success = true,
            Data = order
        });
    }

    /// <summary>
    /// Hủy đơn hàng
    /// </summary>
    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(string id, [FromBody] CancelOrderRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _orderService.CancelOrderAsync(userId, id, request.Reason);

        if (result.Success)
            return Ok(result);

        return result.ErrorCode switch
        {
            ApiErrorCode.NotFound => NotFound(result),
            ApiErrorCode.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}