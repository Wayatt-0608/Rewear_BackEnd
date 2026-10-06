using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/admin/payments")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
public class PaymentAdminController : ControllerBase
{
    private readonly IPayOsService _payOsService;
    private readonly IPaymentGatewayConfig _gatewayConfig;
    private readonly PayOsSettings _payOsSettings;
    private readonly ILogger<PaymentAdminController> _logger;

    public PaymentAdminController(
        IPayOsService payOsService,
        IPaymentGatewayConfig gatewayConfig,
        PayOsSettings payOsSettings,
        ILogger<PaymentAdminController> logger)
    {
        _payOsService = payOsService;
        _gatewayConfig = gatewayConfig;
        _payOsSettings = payOsSettings;
        _logger = logger;
    }

    [HttpPost("webhook/confirm")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> ConfirmWebhook([FromBody] ConfirmWebhookRequest? request)
    {
        // Nếu không truyền URL thì dùng URL mặc định từ config.
        var baseUrl = !string.IsNullOrWhiteSpace(_payOsSettings.AppBaseUrl)
            ? _payOsSettings.AppBaseUrl
            : _gatewayConfig.AppBaseUrl;

        var webhookUrl = !string.IsNullOrWhiteSpace(request?.WebhookUrl)
            ? request.WebhookUrl
            : $"{baseUrl.TrimEnd('/')}/api/payments/webhook";

        _logger.LogInformation(
            "Admin yeu cau confirm webhook URL: {WebhookUrl}",
            webhookUrl);

        var success = await _payOsService.ConfirmWebhookAsync(webhookUrl);

        if (!success)
        {
            return BadRequest(new ApiResponse
            {
                Success = false,
                Message = $"PayOS từ chối URL webhook: {webhookUrl}. Kiểm tra URL có public HTTPS không, và endpoint có trả 200 OK không."
            });
        }

        return Ok(new ApiResponse
        {
            Success = true,
            Message = $"Đã đăng ký webhook với PayOS thành công: {webhookUrl}",
            Data = new { webhookUrl }
        });
    }

    [HttpPost("webhook/confirm/default")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> ConfirmDefaultWebhook()
    {
        return await ConfirmWebhook(null);
    }
}

public class ConfirmWebhookRequest
{
    public string? WebhookUrl { get; set; }
}
