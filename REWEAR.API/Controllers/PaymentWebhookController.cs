using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/payments/webhook")]
public class PaymentWebhookController(IPaymentService payments, IPaymentRepository repository, PayOsSettings settings, ILogger<PaymentWebhookController> logger) : ControllerBase
{
    [HttpPost, RequestSizeLimit(65536)]
    public async Task<IActionResult> Receive()
    {
        if (Request.ContentLength > 65536) return BadRequest(new { error = "Payload too large" });
        try
        {
            using var document = await JsonDocument.ParseAsync(Request.Body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || !root.TryGetProperty("signature", out var sig) || sig.ValueKind != JsonValueKind.String
                || !PayOsSignature.Verify(data, sig.GetString(), settings.ChecksumKey))
                return BadRequest(new { error = "Invalid signature" });
            if (!data.TryGetProperty("paymentLinkId", out var link) || link.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(link.GetString()))
                return BadRequest(new { error = "Missing paymentLinkId" });
            var payment = await repository.GetByPaymentLinkIdAsync(link.GetString()!);
            if (payment == null)
            {
                // payOS sends a signed sample during webhook registration. Unknown links never change business state.
                logger.LogInformation("Đã nhận thông báo có chữ ký nhưng chưa có phiên tương ứng; không thay đổi đơn.");
                return Ok(new { error = (string?)null, data = new { ignored = true } });
            }
            if (payment.GatewayOrderCode > 0 && (!data.TryGetProperty("orderCode", out var code) || code.ToString() != payment.GatewayOrderCode.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                return Conflict(new { error = "Order mismatch" });
            if (data.TryGetProperty("code", out var status) && status.GetString() == "00")
            {
                if (!data.TryGetProperty("currency", out var currency) || currency.ValueKind != JsonValueKind.String || currency.GetString() != "VND")
                    return BadRequest(new { error = "Invalid currency" });
                if (!data.TryGetProperty("amount", out var amount) || !amount.TryGetDecimal(out var received) || received <= 0)
                    return BadRequest(new { error = "Invalid amount" });
                var reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;
                if (string.IsNullOrWhiteSpace(reference)) return BadRequest(new { error = "Missing transaction reference" });
                var result = await payments.HandleWebhookSuccessAsync(payment.OrderCode, payment.PaymentLinkId!, received, reference);
                if (!result.Success) return Conflict(new { error = "Payment processing conflict" });
            }
            else await payments.ReconcileAsync(payment.OrderId);
            return Ok(new { error = (string?)null, data = new { processed = true } });
        }
        catch (JsonException) { return BadRequest(new { error = "Invalid JSON" }); }
        catch (Exception)
        {
            logger.LogError("Không xử lý được thông báo thanh toán; cần thử lại và đối soát.");
            return StatusCode(500, new { error = "Processing unavailable" });
        }
    }
}
