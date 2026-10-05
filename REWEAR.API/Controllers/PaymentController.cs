using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly IPayOsService _payOsService;
    private readonly IOrderService _orderService;
    private readonly IPaymentGatewayConfig _gatewayConfig;
    private readonly PayOsSettings _payOsSettings;

    public PaymentController(
        IPaymentService paymentService,
        IPayOsService payOsService,
        IOrderService orderService,
        IPaymentGatewayConfig gatewayConfig,
        PayOsSettings payOsSettings)
    {
        _paymentService = paymentService;
        _payOsService = payOsService;
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
    /// Tra cứu trạng thái thanh toán của đơn.
    /// </summary>
    /// <remarks>
    /// Frontend gọi endpoint này sau khi PayOS chuyển khách hàng quay lại.
    /// Đây là đường xác nhận thứ 2 bên cạnh webhook, xử lý được trường hợp webhook bị mất.
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

    // ============================================
    // WEBHOOK PAYOS
    // ============================================

    /// <summary>
    /// Webhook nhận thông báo giao dịch từ PayOS.
    /// </summary>
    /// <remarks>
    /// Đặt ngoài [Authorize] vì PayOS gọi không có token JWT.
    /// Bắt buộc xác minh chữ ký trước khi xử lý bất kỳ dữ liệu nào.
    /// </remarks>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PayOsWebhook()
    {
        // Đọc raw body để xác minh chữ ký. Verify trên JSON đã deserialize
        // có thể sai vì thứ tự key / khoảng trắng bị đổi.
        using var reader = new StreamReader(Request.Body);
        string rawBody = await reader.ReadToEndAsync();

        string signature = Request.Headers["x-signature"].ToString();

        // Chữ ký sai -> từ chối, KHÔNG cập nhật dữ liệu.
        if (!_payOsService.VerifyWebhookSignature(rawBody, signature))
        {
            return Ok(new { success = false, message = "Invalid signature" });
        }

        PayOsWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PayOsWebhookPayload>(rawBody);
        }
        catch (JsonException)
        {
            return BadRequest(new { success = false, message = "Invalid payload" });
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.Data?.OrderCode))
            return BadRequest(new { success = false, message = "Missing orderCode" });

        var data = payload.Data;

        // PayOS dùng mã "00" ở tầng ngoài cùng để báo thanh toán thành công.
        if (payload.Code == "00")
        {
            var result = await _paymentService.HandleWebhookSuccessAsync(
                data.OrderCode,
                data.Id ?? string.Empty,
                data.Amount / 100m);

            return Ok(new { success = result.Success, message = result.Message });
        }

        var failResult = await _paymentService.HandleWebhookFailedAsync(
            data.OrderCode, data.CancelReason);

        return Ok(new { success = failResult.Success, message = failResult.Message });
    }
}

/// <summary>
/// Payload webhook mà PayOS gửi lên.
/// </summary>
public class PayOsWebhookPayload
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public PayOsWebhookData? Data { get; set; }
}

/// <summary>
/// Phần data của webhook PayOS.
/// </summary>
public class PayOsWebhookData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>Số tiền tính bằng đồng (PayOS gửi số nguyên).</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("cancelReason")]
    public string? CancelReason { get; set; }
}