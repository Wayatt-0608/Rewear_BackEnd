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
/// Creates payment links and queries authoritative gateway state. Webhook signatures
/// are verified by the API; automatic refunds remain disabled pending a verified contract.
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
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var gatewayUri) || gatewayUri.Scheme != "https"
            || !gatewayUri.Host.EndsWith(".payos.vn", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cần cấu hình URL HTTPS thuộc payOS.");
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
        DateTime expiredAt, long? numericOrderCode = null)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > int.MaxValue || expiredAt <= DateTime.UtcNow
            || string.IsNullOrWhiteSpace(_settings.ChecksumKey)) return null;
        long amountVnd = (long)amount;

        // orderCode phải là SỐ NGUYÊN DUY NHẤT. PayOS từ chối nếu trùng.
        // The application supplies a durable gateway identity for retries.
        numericOrderCode ??= System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);

        // PayOS yêu cầu signature cho body request (HMAC-SHA256).
        // Công thức chính thức:
        //   signature = HMAC_SHA256(checksumKey,
        //     "amount={amount}&cancelUrl={url}&description={desc}&orderCode={code}&returnUrl={url}")
        // Thứ tự alphabet BẮT BUỘC: amount, cancelUrl, description, orderCode, returnUrl
        // PayOS giới hạn description TỐI ĐA 25 ký tự (không tính khoảng trắng thừa).
        // Mã đơn của ta có dạng "RW-20261006-ECE95D" = 18 ký tự.
        // Format gọn: "DH RW-XXX" (chỉ lấy phần cuối của orderCode).
        // Ví dụ: RW-20261006-ECE95D → "DH ECE95D" = 9 ký tự.
        string shortOrderId = orderCode;
        var lastDash = orderCode.LastIndexOf('-');
        if (lastDash >= 0 && lastDash < orderCode.Length - 1)
        {
            shortOrderId = orderCode[(lastDash + 1)..];
        }
        string description = $"DH {shortOrderId}".Trim();

        // Đảm bảo description không vượt quá 25 ký tự (phòng trường hợp shortOrderId dài bất thường).
        if (description.Length > 25)
        {
            description = description[..25];
        }
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
            using var response = await PostAsync("/v2/payment-requests", payload);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cổng thanh toán trả lỗi hoặc dữ liệu không hợp lệ.");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // PayOS có thể trả 200 OK với body lỗi (code != "00") nhưng không có field data.
            // Phải kiểm tra code trước khi truy cập data.
            var code = root.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
            if (code != "00")
            {
                _logger.LogWarning("PayOS từ chối tạo phiên thanh toán. Không ghi nội dung phản hồi.");
                return null;
            }

            // Một số response lỗi vẫn trả 200 nhưng không có field data
            // (ví dụ: đơn hàng đã tồn tại, signature sai, v.v.)
            if (!root.TryGetProperty("data", out var data))
            {
                _logger.LogWarning("Cổng thanh toán trả lỗi hoặc dữ liệu không hợp lệ.");
                return null;
            }

            if (!root.TryGetProperty("signature", out var responseSignature) || !PayOsSignature.Verify(data, responseSignature.GetString(), _settings.ChecksumKey))
            { _logger.LogWarning("Không xác minh được chữ ký phản hồi tạo thanh toán."); return null; }

            // Mapping theo docs PayOS https://payos.vn/docs/api:
            //   data.id            = paymentLinkId (string UUID) - DÙNG ĐỂ TRA CỨU
            //   data.paymentLinkId = paymentLinkId (alias, một số version trả về)
            //   data.orderCode     = numeric orderCode (số nguyên)
            //
            // Lấy paymentLinkId từ field `id` (mặc định) hoặc `paymentLinkId` (alias).
            string paymentLinkId = "";
            if (data.TryGetProperty("id", out var id))
                paymentLinkId = id.GetString() ?? "";
            else if (data.TryGetProperty("paymentLinkId", out var link))
                paymentLinkId = link.GetString() ?? "";

            // orderCode là số nguyên do backend gửi lên.
            string orderCodeStr = data.TryGetProperty("orderCode", out var oc)
                ? oc.ToString() : "";
            if (string.IsNullOrWhiteSpace(paymentLinkId) || orderCodeStr != numericOrderCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || !data.TryGetProperty("amount", out var signedAmount) || signedAmount.GetDecimal() != amount) return null;

            return new PayOsCreateResult
            {
                Id = paymentLinkId,
                PaymentLinkId = paymentLinkId,
                CheckoutUrl = data.TryGetProperty("checkoutUrl", out var url) ? url.GetString() ?? "" : "",
                QrCode = data.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Không nhận được phản hồi tạo thanh toán hợp lệ ({Type}).", ex.GetType().Name);
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
    /// Tra cứu trạng thái payment request: GET /v2/payment-requests/{id}
    /// </summary>
    /// <remarks>
    /// Trả về status (PENDING/PAID/CANCELLED/EXPIRED) và amountPaid.
    /// Cần endpoint này để backend tự đối soát với PayOS khi không có webhook:
    /// FE gọi GET /api/payments/{id}/status → backend gọi API này → nếu đã
    /// PAID thì chốt đơn luôn (HandleWebhookSuccessAsync).
    /// </remarks>
    public async Task<PayOsTransactionInfo?> GetTransactionAsync(string transactionId)
    {
        try
        {
            using var response = await GetAsync($"/v2/payment-requests/{Uri.EscapeDataString(transactionId)}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PayOS tra cuu payment request that bai. PaymentRequestId: {PaymentRequestId}, Status: {Status}",
                    transactionId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // PayOS có thể trả 200 OK với body lỗi (code != "00") nhưng không có field data.
            if (!root.TryGetProperty("code", out var codeProp)
                || codeProp.GetString() != "00")
            {
                _logger.LogWarning("PayOS chưa trả về trạng thái giao dịch hợp lệ.");
                return null;
            }

            var data = root.GetProperty("data");

            // PayOS trả cả `paidAmount` (alias cũ) và `amountPaid` (chuẩn mới).
            // Đọc amountPaid trước, fallback paidAmount để tương thích ngược.
            decimal paidAmount = 0m;
            if (data.TryGetProperty("amountPaid", out var amountPaid))
                paidAmount = amountPaid.GetDecimal();
            else if (data.TryGetProperty("paidAmount", out var paidAmountOld))
                paidAmount = paidAmountOld.GetDecimal();

            return new PayOsTransactionInfo
            {
                Id = data.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Reference = data.TryGetProperty("orderCode", out var oc)
                    ? oc.ToString() : "",
                PaymentLinkId = data.TryGetProperty("id", out var link)
                    ? link.GetString() ?? "" : "",
                Amount = data.TryGetProperty("amount", out var amt) ? amt.GetInt32() : 0,
                PaidAmount = paidAmount,
                Status = data.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                CompletedAt = data.TryGetProperty("transactionDateTime", out var ca)
                    && ca.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(ca.GetString(), out var parsed)
                        ? parsed
                        : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Không tra cứu được trạng thái thanh toán ({Type}).", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Automatic refund is intentionally disabled until a supported merchant contract is verified.
    /// </summary>
    public Task<PayOsRefundResult> RefundAsync(string transactionId, string? reason) =>
        Task.FromResult(new PayOsRefundResult { Success = false, Message = "Hoàn tiền cần được đối soát và xác nhận theo chứng từ; chưa bật API hoàn tiền tự động." });

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

        request.Headers.UserAgent.ParseAdd("REWEAR/1.0");

        return request;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object payload)
    {
        var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        // Retry tối đa 3 lần với backoff khi gặp lỗi Cloudflare 5xx / DNS / 530.
        // Lỗi 530 (Origin DNS error) thường là tạm thời do load balancer PayOS.
        const int maxRetries = 3;
        HttpResponseMessage? response = null;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            response = await _httpClient.SendAsync(request);
            int status = (int)response.StatusCode;
            if (status < 500 && status != 429)
            {
                return response;
            }
            // 5xx hoặc 429 (rate limit): retry.
            _logger.LogWarning(
                "PayOS request that bai lan {Attempt}/{Max}, status {Status}. Do retry...",
                attempt, maxRetries, status);
            if (attempt < maxRetries)
            {
                response.Dispose(); request.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
                // Phải clone request vì body đã được đọc.
                request = CreateRequest(HttpMethod.Post, path);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    new MediaTypeHeaderValue("application/json"));
            }
        }
        return response!;
    }

    private Task<HttpResponseMessage> GetAsync(string path)
        => _httpClient.SendAsync(CreateRequest(HttpMethod.Get, path));

    /// <summary>
    /// Đăng ký URL webhook với PayOS. PayOS sẽ test URL ngay khi nhận request,
    /// nếu endpoint trả response đúng format thì URL được lưu lại.
    /// </summary>
    /// <remarks>
    /// POST /confirm-webhook body: { "webhookUrl": "https://..." }
    /// Response: { "code": "00", "desc": "success", "data": {...} }
    /// </remarks>
    public async Task<bool> ConfirmWebhookAsync(string webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogError("ConfirmWebhook: webhookUrl rong");
            return false;
        }

        try
        {
            var payload = new Dictionary<string, object>
            {
                ["webhookUrl"] = webhookUrl
            };

            using var response = await PostAsync("/confirm-webhook", payload);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cổng thanh toán trả lỗi hoặc dữ liệu không hợp lệ.");
                return false;
            }

            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (code != "00")
            {
                _logger.LogWarning("PayOS chưa chấp thuận đăng ký webhook.");
                return false;
            }

            _logger.LogInformation("PayOS đã chấp thuận đăng ký webhook.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Chưa đăng ký được webhook ({Type}).", ex.GetType().Name);
            return false;
        }
    }
}
