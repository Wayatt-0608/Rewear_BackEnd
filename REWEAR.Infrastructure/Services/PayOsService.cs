using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Implementation IPayOsService gọi REST API của PayOS (Task 8).
/// </summary>
/// <remarks>
/// Hệ thống không nhận webhook nữa, chỉ gọi đi: tạo phiên, tra cứu trạng thái,
/// hoàn tiền. Tất cả đều qua REST API của PayOS.
/// </remarks>
public class PayOsService : IPayOsService
{
    private readonly HttpClient _httpClient;
    private readonly PayOsSettings _settings;
    private readonly ILogger<PayOsService> _logger;

    public PayOsService(
        HttpClient httpClient,
        PayOsSettings settings,
        ILogger<PayOsService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Tạo payment request: POST /v2/payment-requests
    /// </summary>
    public async Task<PayOsCreateResult?> CreatePaymentRequestAsync(
        string orderCode, decimal amount,
        string returnUrl, string cancelUrl,
        DateTime expiredAt)
    {
        // PayOS dùng đơn vị tiền tệ là VND (số nguyên).
        long amountVnd = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

        // orderCode phải là SỐ NGUYÊN DUY NHẤT. PayOS từ chối nếu trùng.
        // Dùng Unix timestamp + random suffix để đảm bảo unique.
        long numericOrderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 9_000_000_000L + 1_000_000_000L;

        // PayOS yêu cầu signature cho body request (HMAC-SHA256).
        // Công thức chính thức:
        //   signature = HMAC_SHA256(checksumKey,
        //     "amount={amount}&cancelUrl={url}&description={desc}&orderCode={code}&returnUrl={url}")
        // Thứ tự alphabet BẮT BUỘC: amount, cancelUrl, description, orderCode, returnUrl
        string description = $"Thanh toan don {orderCode}";
        string signatureData =
            $"amount={amountVnd}" +
            $"&cancelUrl={cancelUrl}" +
            $"&description={description}" +
            $"&orderCode={numericOrderCode}" +
            $"&returnUrl={returnUrl}";
        string signature = ComputeHmacSha256(_settings.ChecksumKey, signatureData);

        var payload = new Dictionary<string, object>
        {
            ["orderCode"] = numericOrderCode,
            ["amount"] = amountVnd,
            ["description"] = description,
            ["returnUrl"] = returnUrl,
            ["cancelUrl"] = cancelUrl,
            ["signature"] = signature,
            // Hết hạn ở phía PayOS: Unix timestamp (seconds).
            ["expiredAt"] = new DateTimeOffset(expiredAt.ToUniversalTime()).ToUnixTimeSeconds()
        };

        try
        {
            var response = await PostAsync("/v2/payment-requests", payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "PayOS create payment request that bai voi ma don {OrderCode}. Status: {Status}, Body: {Body}, Payload: {Payload}",
                    orderCode, (int)response.StatusCode, body, System.Text.Json.JsonSerializer.Serialize(payload));
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            return new PayOsCreateResult
            {
                Id = data.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                PaymentLinkId = data.TryGetProperty("paymentLinkId", out var link)
                    ? link.GetString() ?? "" : "",
                CheckoutUrl = data.TryGetProperty("checkoutUrl", out var url) ? url.GetString() ?? "" : "",
                QrCode = data.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loi khi goi PayOS tao payment request cho don {OrderCode}", orderCode);
            return null;
        }
    }

    /// <summary>
    /// Tính HMAC-SHA256 signature cho PayOS.
    /// </summary>
    private static string ComputeHmacSha256(string key, string data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Tra cứu giao dịch: GET /v1/transactions/{id}
    /// </summary>
    public async Task<PayOsTransactionInfo?> GetTransactionAsync(string transactionId)
    {
        try
        {
            var response = await GetAsync($"/v1/transactions/{transactionId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS tra cuu giao dich that bai. TransactionId: {TransactionId}, Status: {Status}",
                    transactionId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            return new PayOsTransactionInfo
            {
                Id = data.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Reference = data.TryGetProperty("reference", out var rf) ? rf.GetString() ?? "" : "",
                PaymentLinkId = data.TryGetProperty("paymentLinkId", out var link) ? link.GetString() ?? "" : "",
                Amount = data.TryGetProperty("amount", out var amt) ? amt.GetInt32() : 0,
                PaidAmount = data.TryGetProperty("paidAmount", out var paid) ? paid.GetDecimal() : 0m,
                Status = data.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                CompletedAt = data.TryGetProperty("completedAt", out var ca)
                    && ca.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(ca.GetString(), out var parsed)
                        ? parsed
                        : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loi khi tra cuu giao dich PayOS {TransactionId}", transactionId);
            return null;
        }
    }

    /// <summary>
    /// Hoàn tiền: POST /v1/refunds
    /// </summary>
    public async Task<PayOsRefundResult> RefundAsync(string transactionId, string? reason)
    {
        // Signature cho refund: amount=... (optional) & description=... & transactionId=...
        // Nếu không truyền amount thì PayOS hoàn toàn bộ.
        // Format: signature = HMAC_SHA256(checksumKey, "transactionId=xxx")
        string signatureData = $"transactionId={transactionId}";
        if (!string.IsNullOrWhiteSpace(reason))
        {
            signatureData = $"description={reason}&{signatureData}";
        }
        string signature = ComputeHmacSha256(_settings.ChecksumKey, signatureData);

        var payload = new Dictionary<string, object>
        {
            ["transactionId"] = transactionId,
            ["signature"] = signature
        };

        if (!string.IsNullOrWhiteSpace(reason))
            payload["description"] = reason;

        try
        {
            var response = await PostAsync("/v1/refunds", payload);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "PayOS hoan tien that bai. TransactionId: {TransactionId}, Body: {Body}",
                    transactionId, body);

                return new PayOsRefundResult
                {
                    Success = false,
                    Message = "PayOS tu choi yeu cau hoan tien."
                };
            }

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");

            return new PayOsRefundResult
            {
                Success = true,
                RefundId = data.TryGetProperty("id", out var rid) ? rid.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loi khi goi PayOS hoan tien giao dich {TransactionId}", transactionId);
            return new PayOsRefundResult
            {
                Success = false,
                Message = "Khong ket noi duoc PayOS de hoan tien."
            };
        }
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Tạo HttpRequestMessage với header xác thực mà PayOS yêu cầu cho mọi API:
    /// Authorization chứa Partner-ID, cộng thêm API key và checksum.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_settings.BaseUrl}{path}");

        // PayOS dùng 2 header riêng thay vì Authorization.
        // Docs: https://payos.vn/docs/api/
        request.Headers.TryAddWithoutValidation("x-client-id", _settings.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", _settings.ApiKey);

        // User-Agent hợp lệ: Cloudflare origin có thể block request không có UA
        // hoặc có UA giống bot (.NET HttpClient default).
        request.Headers.TryAddWithoutValidation(
            "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        return request;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object payload)
    {
        var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        return await _httpClient.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetAsync(string path)
        => _httpClient.SendAsync(CreateRequest(HttpMethod.Get, path));
}
