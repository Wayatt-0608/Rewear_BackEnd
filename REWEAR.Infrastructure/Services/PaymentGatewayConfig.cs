using REWEAR.Application.Interfaces;
using REWEAR.Infrastructure.Payments;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Cấu hình cổng thanh toán đọc từ appsettings.json (Task 8).
/// </summary>
public class PaymentGatewayConfig : IPaymentGatewayConfig
{
    private readonly PayOsSettings _payOsSettings;

    public PaymentGatewayConfig(PayOsSettings payOsSettings)
    {
        _payOsSettings = payOsSettings;
    }

    /// <summary>
    /// Base URL ứng dụng. Dùng cùng BaseUrl của PayOS làm mặc định khi phát triển
    /// cục bộ, vì khi đó PayOS chỉ cần trả về trang thanh toán chứ chưa gọi ngược lại.
    /// </summary>
    public string AppBaseUrl => string.IsNullOrWhiteSpace(_payOsSettings.AppBaseUrl)
        ? _payOsSettings.BaseUrl
        : _payOsSettings.AppBaseUrl;
}