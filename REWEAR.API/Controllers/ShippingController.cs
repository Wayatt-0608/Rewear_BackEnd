using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/shipping")]
[Authorize]
public class ShippingController : ControllerBase
{
    private readonly IShippingService _shippingService;
    private readonly IOrderService _orderService;

    public ShippingController(IShippingService shippingService, IOrderService orderService)
    {
        _shippingService = shippingService;
        _orderService = orderService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // VẬN CHUYỂN (Task 9)
    // ============================================

    /// <summary>
    /// Lấy bảng giá vận chuyển để hiển thị lựa chọn khi checkout.
    /// </summary>
    /// <remarks>REWEAR miễn phí vận chuyển toàn bộ nên phí luôn = 0.</remarks>
    [HttpGet("rates")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> GetRates()
    {
        var rates = await _shippingService.GetRatesAsync();
        return Ok(new ApiResponse { Success = true, Data = rates });
    }

    /// <summary>
    /// Lấy thông tin vận chuyển của một đơn (chỉ xem được đơn của chính mình).
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần xem thông tin vận chuyển.</param>
    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> GetShipping(string orderId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var shipping = await _shippingService.GetByOrderIdAsync(orderId, userId);

        if (shipping == null)
            return NotFound(new ApiResponse
            {
                Success = false,
                Message = "Không tìm thấy thông tin vận chuyển."
            });

        return Ok(new ApiResponse { Success = true, Data = shipping });
    }

    /// <summary>
    /// Tạo thông tin vận chuyển cho đơn (chọn phương thức giao hàng).
    /// </summary>
    /// <remarks>Chỉ được tạo khi đơn đã thanh toán thành công.</remarks>
    /// <param name="orderId">Id của đơn hàng cần tạo vận chuyển.</param>
    /// <param name="method">Phương thức vận chuyển (Standard / Express).</param>
    [HttpPost("{orderId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> CreateShipping(
        string orderId, [FromQuery] ShippingMethod method)
    {
        var result = await _shippingService.CreateShippingAsync(orderId, method);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật vận chuyển: nhập mã vận đơn (đang chuyển) hoặc ghi chú giao hàng.
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần cập nhật.</param>
    /// <param name="request">Đơn vị vận chuyển, mã vận đơn, ngày dự kiến giao và ghi chú.</param>
    [HttpPut("{orderId}")]
    [Authorize(Policy = "ShippingOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> UpdateShipping(
        string orderId, [FromBody] UpdateShippingRequest request)
    {
        var result = await _shippingService.UpdateShippingAsync(orderId, request);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật trạng thái đơn hàng (gửi hàng, giao xong) và ghi vào timeline.
    /// </summary>
    /// <remarks>
    /// Chuyển sang Shipping bắt buộc có mã vận đơn. Chuyển sang Delivered sẽ chốt
    /// sản phẩm sang Sold.
    /// </remarks>
    /// <param name="orderId">Id của đơn hàng cần cập nhật trạng thái.</param>
    /// <param name="request">Trạng thái mới, ghi chú, mã vận đơn và ngày dự kiến giao.</param>
    [HttpPut("{orderId}/status")]
    [Authorize(Policy = "ShippingOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> UpdateOrderStatus(
        string orderId, [FromBody] UpdateOrderStatusRequest request)
    {
        var userId = GetUserId();

        var result = await _orderService.UpdateStatusAsync(
            orderId, request.Status, request.Note,
            request.TrackingNumber, request.EstimatedDeliveryDate,
            OrderStatusChangedBy.Staff, userId);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }
}