using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IOrderService _orderService;
    private readonly IPaymentGatewayConfig _gatewayConfig;
    private readonly PayOsSettings _payOsSettings;

    public PaymentController(
        IPaymentService paymentService,
        IOrderService orderService,
        IPaymentGatewayConfig gatewayConfig,
        PayOsSettings payOsSettings)
    {
        _paymentService = paymentService;
        _orderService = orderService;
        _gatewayConfig = gatewayConfig;
        _payOsSettings = payOsSettings;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // THANH TOÁN (Task 8)
    // ============================================

    /// <summary>
    /// Tạo phiên thanh toán PayOS cho một đơn hàng.
    /// </summary>
    /// <remarks>
    /// Trả về checkoutUrl để frontend chuyển khách hàng tới trang thanh toán,
    /// kèm qrCode và thời hạn 15 phút. Nếu hết hạn, sản phẩm tự động trở lại cửa hàng.
    /// </remarks>
    /// <param name="orderId">Id của đơn hàng cần tạo phiên thanh toán.</param>
    [HttpPost("{orderId}/session")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> CreateSession(string orderId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        // Chỉ chủ đơn mới tạo được phiên thanh toán.
        var ownedOrder = await _orderService.GetOrderDetailAsync(userId, orderId);
        if (ownedOrder == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        var (result, session) = await _paymentService.CreateSessionAsync(
            orderId, _payOsSettings.ExpirationMinutes);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                _ => BadRequest(result)
            };
        }

        return Ok(new ApiResponse
        {
            Success = true,
            Message = "Tạo phiên thanh toán thành công.",
            Data = session
        });
    }

    /// <summary>
    /// Tra cứu trạng thái thanh toán của đơn. Frontend gọi polling mỗi 3-5 giây.
    /// </summary>
    /// <remarks>
    /// Thay cho webhook: backend tự hỏi PayOS server trực tiếp. Nếu đơn đang
    /// Pending thì gọi PayOS lấy status thật (PAID/CANCELLED/EXPIRED) và cập nhật
    /// DB luôn. Idempotent: gọi nhiều lần cũng chỉ chốt đơn 1 lần.
    /// </remarks>
    /// <param name="orderId">Id của đơn hàng cần tra cứu trạng thái thanh toán.</param>
    [HttpGet("{orderId}/status")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> GetStatus(string orderId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var status = await _paymentService.GetStatusAsync(orderId, userId);

        if (status == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        return Ok(new ApiResponse { Success = true, Data = status });
    }

    /// <summary>
    /// Lấy lịch sử thanh toán của đơn (chỉ xem được đơn của chính mình).
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần xem lịch sử thanh toán.</param>
    [HttpGet("{orderId}/history")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> GetHistory(string orderId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var history = await _paymentService.GetHistoryAsync(orderId, userId);

        if (history == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy đơn hàng." });

        return Ok(new ApiResponse { Success = true, Data = history });
    }
}
