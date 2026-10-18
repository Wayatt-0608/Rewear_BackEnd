using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IPaymentService _paymentService;
    private readonly PayOsSettings _payOsSettings;

    public OrdersController(
        IOrderService orderService,
        IPaymentService paymentService,
        PayOsSettings payOsSettings)
    {
        _orderService = orderService;
        _paymentService = paymentService;
        _payOsSettings = payOsSettings;
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
    /// <remarks>
    /// REWEAR áp dụng mô hình trả tiền trước: sau khi tạo đơn, hệ thống tự gọi
    /// PayOS tạo phiên thanh toán và trả về cùng response. Sản phẩm được giữ chỗ
    /// trong 5 phút (DEV/TEST); quá hạn thì tự động trở lại cửa hàng.
    /// </remarks>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _orderService.CheckoutAsync(userId, request);

        if (!result.Success)
        {
            // 404 khi không tìm thấy địa chỉ, 409 khi có xung đột tồn kho,
            // còn lại (giỏ rỗng, giỏ sai) là 400.
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                _ => BadRequest(result)
            };
        }

        // Lấy orderId từ response để tạo phiên thanh toán PayOS.
        var orderData = result.Data as OrderResponse;
        if (orderData == null)
            return Ok(result);

        var (sessionResult, session) = await _paymentService.CreateSessionAsync(
            orderData.Id, _payOsSettings.ExpirationMinutes);

        if (!sessionResult.Success)
        {
            // Đơn đã bị huỷ tự động và trả kho vì không tạo được phiên thanh toán.
            return sessionResult.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(sessionResult),
                ApiErrorCode.Conflict => Conflict(sessionResult),
                _ => BadRequest(sessionResult)
            };
        }

        return Ok(new ApiResponse
        {
            Success = true,
            Message = result.Message,
            Data = new
            {
                Order = orderData,
                Payment = session
            }
        });
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
    /// <remarks>
    /// Đơn chưa thanh toán: hủy trực tiếp, sản phẩm trở lại cửa hàng.
    /// Đơn đã thanh toán: hệ thống tự hoàn tiền qua PayOS rồi mới hủy.
    /// </remarks>
    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(string id, [FromBody] CancelOrderRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        // Kiểm tra đơn có thuộc về user này không trước khi xử lý.
        var order = await _orderService.GetOrderDetailAsync(userId, id);
        if (order == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        // Đơn đã thu tiền -> phải hoàn tiền qua PayOS trước (PaymentService lo).
        if (order.Status != nameof(OrderStatus.AwaitingPayment))
        {
            var refundResult = await _paymentService.RefundAsync(id, request.Reason);

            if (!refundResult.Success)
            {
                return refundResult.ErrorCode switch
                {
                    ApiErrorCode.NotFound => NotFound(refundResult),
                    ApiErrorCode.Conflict => Conflict(refundResult),
                    _ => BadRequest(refundResult)
                };
            }

            return Ok(refundResult);
        }

        // Đơn chưa thu tiền -> hủy bình thường.
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

    // ============================================
    // ORDER TRACKING (Task 7)
    // ============================================

    /// <summary>
    /// Lấy thông tin theo dõi đơn: mã vận đơn, ngày dự kiến giao và timeline trạng thái.
    /// </summary>
    /// <param name="id">Id của đơn hàng cần theo dõi.</param>
    [HttpGet("{id}/tracking")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTracking(string id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var tracking = await _orderService.GetTrackingAsync(userId, id);

        if (tracking == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        return Ok(new ApiResponse { Success = true, Data = tracking });
    }
}