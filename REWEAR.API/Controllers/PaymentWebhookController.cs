using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/payments/webhook")]
public class PaymentWebhookController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly PayOsSettings _payOsSettings;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentService paymentService,
        PayOsSettings payOsSettings,
        ILogger<PaymentWebhookController> logger)
    {
        _paymentService = paymentService;
        _payOsSettings = payOsSettings;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Receive()
    {
        // Đọc raw body để verify signature (parse JSON sau khi verify).
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("PayOS webhook: empty body");
            return Ok(new { error = "Empty body", data = (object?)null });
        }

        // Verify signature từ PayOS để chắc chắn request thật.
        if (!VerifyPayOsSignature(body, out var dataElement, out var errorMessage))
        {
            _logger.LogWarning("PayOS webhook: signature invalid - {Error}", errorMessage);
            // Theo PayOS docs: trả 400 + {error: ..., data: null}.
            return BadRequest(new { error = errorMessage, data = (object?)null });
        }

        // Lấy thông tin từ data.
        var code = dataElement.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
        var orderCode = dataElement.TryGetProperty("orderCode", out var oc) ? oc.ToString() : "";
        var amount = dataElement.TryGetProperty("amount", out var amt) ? amt.GetDecimal() : 0m;
        var paymentLinkId = dataElement.TryGetProperty("paymentLinkId", out var pl) ? pl.GetString() ?? "" : "";

        _logger.LogInformation(
            "PayOS webhook received: orderCode={OrderCode}, code={Code}, amount={Amount}, paymentLinkId={PaymentLinkId}",
            orderCode, code, amount, paymentLinkId);

        if (string.IsNullOrWhiteSpace(paymentLinkId))
        {
            _logger.LogWarning("PayOS webhook: missing paymentLinkId");
            return Ok(new { error = "Missing paymentLinkId", data = (object?)null });
        }

        // Map paymentLinkId (PayOS) -> orderCode (REWEAR) thông qua DB.
        var orderCodeRewear = await ResolveOrderCodeAsync(paymentLinkId);
        if (string.IsNullOrWhiteSpace(orderCodeRewear))
        {
            _logger.LogWarning(
                "PayOS webhook: khong tim thay payment voi paymentLinkId={PaymentLinkId}",
                paymentLinkId);
            // Vẫn trả 200 OK để PayOS không retry, nhưng báo error.
            return Ok(new { error = "Payment not found in DB", data = (object?)null });
        }

        // Xử lý theo code.
        try
        {
            if (code == "00")
            {
                // Thanh toán thành công.
                var result = await _paymentService.HandleWebhookSuccessAsync(
                    orderCodeRewear, paymentLinkId, amount);
                if (!result.Success)
                {
                    _logger.LogError(
                        "PayOS webhook: xu ly thanh cong that bai cho {OrderCode}: {Message}",
                        orderCodeRewear, result.Message);
                    // Trả error trong body, nhưng status 200 để PayOS không retry
                    // (vì logic đã xử lý - có thể do idempotency).
                    return Ok(new { error = (string?)null, data = new { orderCode = orderCodeRewear, status = "processed" } });
                }
            }
            else
            {
                // Thanh toán thất bại / bị hủy.
                var message = dataElement.TryGetProperty("desc", out var d) ? d.GetString() : null;
                var result = await _paymentService.HandleWebhookFailedAsync(orderCodeRewear, message);
                if (!result.Success)
                {
                    _logger.LogError(
                        "PayOS webhook: xu ly that bai that bai cho {OrderCode}: {Message}",
                        orderCodeRewear, result.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS webhook: exception khi xu ly don {OrderCode}", orderCodeRewear);
            // Trả 200 OK + error để PayOS không retry - vì đã lỗi rồi, retry cũng fail.
            return Ok(new { error = ex.Message, data = (object?)null });
        }

        // Success: trả format PayOS mong đợi.
        return Ok(new { error = (string?)null, data = new { orderCode = orderCodeRewear, status = "success" } });
    }

    private bool VerifyPayOsSignature(string body, out JsonElement dataElement, out string errorMessage)
    {
        dataElement = default;
        errorMessage = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Lấy signature từ request.
            if (!root.TryGetProperty("signature", out var sigProp))
            {
                errorMessage = "Missing signature";
                return false;
            }
            var providedSignature = sigProp.GetString();
            if (string.IsNullOrWhiteSpace(providedSignature))
            {
                errorMessage = "Empty signature";
                return false;
            }

            // Tính signature từ data.
            if (!root.TryGetProperty("data", out dataElement))
            {
                errorMessage = "Missing data";
                return false;
            }

            var expectedSignature = ComputeHmacSha256FromObject(
                dataElement, _payOsSettings.ChecksumKey);

            if (!string.Equals(expectedSignature, providedSignature, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "PayOS signature mismatch. Expected={Expected}, Provided={Provided}",
                    expectedSignature, providedSignature);
                errorMessage = "Signature mismatch";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS webhook: error parsing/verifying");
            errorMessage = "Parse error";
            return false;
        }
    }

    private static string ComputeHmacSha256FromObject(JsonElement element, string key)
    {
        var pairs = new List<string>();
        foreach (var property in element.EnumerateObject())
        {
            // Chỉ lấy primitive (string, number, bool). Bỏ qua object/array lồng nhau.
            if (property.Value.ValueKind == JsonValueKind.Object
                || property.Value.ValueKind == JsonValueKind.Array)
                continue;

            string value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.Number => property.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => property.Value.ToString()
            };
            pairs.Add($"{property.Name}={value}");
        }

        pairs.Sort(StringComparer.Ordinal);
        var data = string.Join("&", pairs);
        return ComputeHmacSha256(key, data);
    }

    private static string ComputeHmacSha256(string key, string data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string?> ResolveOrderCodeAsync(string paymentLinkId)
    {
        try
        {
            var paymentRepo = HttpContext.RequestServices
                .GetService(typeof(REWEAR.Application.Interfaces.IPaymentRepository))
                as REWEAR.Application.Interfaces.IPaymentRepository;

            if (paymentRepo == null)
            {
                _logger.LogError("PayOS webhook: khong the lay IPaymentRepository tu DI");
                return null;
            }

            var payment = await paymentRepo.GetByPaymentLinkIdAsync(paymentLinkId);
            return payment?.OrderCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS webhook: loi khi tim orderCode tu paymentLinkId");
            return null;
        }
    }
}
