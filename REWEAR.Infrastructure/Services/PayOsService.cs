using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Implementation IPayOsService gọi REST API của PayOS (Task 8).
/// </summary>
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

        // Số thứ tự giao dịch tăng dần theo yêu cầu của PayOS.
        int orderNo = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);

        var payload = new Dictionary<string, object>
        {
            ["partnerCode"] = _settings.ClientId,
            ["partnerName"] = "REWEAR",
            ["orderCode"] = orderCode,
            ["orderNo"] = orderNo.ToString(),
            ["amount"] = amountVnd,
            ["currency"] = "VND",
            ["description"] = $"Thanh toan don hang {orderCode}",
            ["returnUrl"] = returnUrl,
            ["cancelUrl"] = cancelUrl,
            // Hết hạn ở phía PayOS: khách không thể thanh toán sau mốc này dù
            // hệ thống bên ta có xử lý chậm.
            ["expiredAt"] = expiredAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        try
        {
            var response = await PostAsync("/v2/payment-requests", payload);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "PayOS create payment request that bai voi ma don {OrderCode}. Status: {Status}, Body: {Body}",
                    orderCode, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
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
    /// Xác minh chữ ký webhook: HMAC-SHA256(checksumKey, rawBody) so với
    /// giá trị header x-signature mà PayOS gửi kèm.
    /// </summary>
    public bool VerifyWebhookSignature(string rawRequestBody, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        if (string.IsNullOrWhiteSpace(_settings.ChecksumKey))
        {
            _logger.LogError("Chua cau hinh PayOs:ChecksumKey nen khong the xac thuc chu ky webhook.");
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.ChecksumKey));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawRequestBody));
        string expected = Convert.ToHexString(hash).ToLowerInvariant();

        // So sánh constant-time để tránh timing attack.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
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
        var payload = new Dictionary<string, object>
        {
            ["partnerCode"] = _settings.ClientId,
            ["transactionId"] = transactionId
        };

        if (!string.IsNullOrWhiteSpace(reason))
            payload["reason"] = reason;

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

        // Header xác thực: partnerCode:apiKey
        request.Headers.TryAddWithoutValidation(
            "Authorization", $"{_settings.ClientId}:{_settings.ApiKey}");

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