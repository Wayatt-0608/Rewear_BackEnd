namespace REWEAR.Infrastructure.Payments;

/// <summary>
/// Cấu hình PayOS - đọc từ appsettings.json (Task 8).
/// Lấy giá trị thật tại https://payos.vn (mục Đăng ký dịch vụ / Merchant).
/// Giai đoạn test dùng tài khoản Sandbox với endpoint api-beta.payos.vn.
/// </summary>
public class PayOsSettings
{
    /// <summary>Client ID (Partner ID) do PayOS cấp khi đăng ký merchant.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>API Key do PayOS cấp.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Checksum Key dùng để tạo và xác minh chữ ký webhook.
    /// KHÔNG BAO GIỜ gửi giá trị này xuống frontend và không commit lên git.
    /// </summary>
    public string ChecksumKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL của PayOS. Production: https://api-merchant.payos.vn (endpoint mới từ PayOS).
    /// Cũ: api.payos.vn (đã lỗi thời, không dùng được).
    /// </summary>
    public string BaseUrl { get; set; } = "https://api-merchant.payos.vn";

    /// <summary>
    /// Base URL của chính ứng dụng, dùng để dựng returnUrl / cancelUrl gửi cho PayOS.
    /// Nếu để trống thì dùng luôn <see cref="BaseUrl"/> (đủ dùng khi phát triển cục bộ).
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian khách được giữ chỗ sản phẩm để hoàn tất thanh toán (phút).
    /// </summary>
    public int ExpirationMinutes { get; set; } = 15;
}